#!/usr/bin/env bash
# The fleet: one Postgres, the migrate role once, then N api and N worker
# replicas from the same build behind a round-robin proxy - the production
# topology (docs/production.md) on one host. Runs the fleet suite against it
# (tests/Premise.FleetTests: replica spread, idempotency and quotas across
# replicas, a replica killed mid-batch, one sweep per period across workers),
# or the load baseline for the scaling table. Tears everything down.
#
#   tools/replica-stack.sh [replicas]                 # fleet suite (default 2)
#   tools/replica-stack.sh [replicas] --bench [s] [c] # load baseline, s seconds x c concurrency
#   ... --pgbouncer [transaction|session]             # api/worker connect through a PgBouncer container
#   ... --pg-cpus N                                   # the CPU quota Postgres gets (default 2)
# Postgres runs with pg_stat_statements loaded, so the bench reports the
# statements the database ran per request.
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
replicas=${1:-2}; shift || true
case "$replicas" in ''|*[!0-9]*) echo "replicas must be a number"; exit 1;; esac
mode=suite; seconds=15; concurrency=32; pgbouncer=""; pg_cpus=${FLEET_PG_CPUS:-2}
while [ $# -gt 0 ]; do
  case "$1" in
    --bench) mode=bench; shift; [ $# -gt 0 ] && [[ "$1" != --* ]] && { seconds=$1; shift; }; [ $# -gt 0 ] && [[ "$1" != --* ]] && { concurrency=$1; shift; } ;;
    --pgbouncer) pgbouncer=transaction; shift; [ $# -gt 0 ] && [[ "$1" != --* ]] && { pgbouncer=$1; shift; } ;;
    --pg-cpus) pg_cpus=$2; shift 2 ;;
    *) echo "unknown argument: $1"; exit 1 ;;
  esac
done

pg_port=${FLEET_PG_PORT:-55433}; bouncer_port=${FLEET_PGBOUNCER_PORT:-56432}; proxy_port=${FLEET_PROXY_PORT:-5300}
api_base=${FLEET_API_PORT_BASE:-5301}; worker_base=${FLEET_WORKER_PORT_BASE:-5401}
owner_cs="Host=localhost;Port=$pg_port;Database=premise;Username=postgres;Password=owner"
log_dir=$(mktemp -d "${TMPDIR:-/tmp}/premise-fleet.XXXXXX")
echo "Fleet logs: $log_dir"
pids=()
cleanup() {
  status=$?
  set +e
  for pid in "${pids[@]:-}"; do [ -n "$pid" ] && kill "$pid" 2>/dev/null; done
  docker rm -f fleet-pg fleet-pgbouncer >/dev/null 2>&1 || true
  docker network rm fleet-net >/dev/null 2>&1 || true
  [ "$status" -ne 0 ] && echo "Failure diagnostics: $log_dir"
  return "$status"
}
trap cleanup EXIT

docker rm -f fleet-pg fleet-pgbouncer >/dev/null 2>&1 || true
docker network rm fleet-net >/dev/null 2>&1 || true
docker network create fleet-net >/dev/null
source "$root/tools/postgres-image.sh"
# connections are budgeted across the fleet (docs/production.md): Postgres
# sized for it, and each process given its share - four replicas of each role
# against the image's default hundred was the first thing the bench found.
# With PgBouncer in front, Postgres keeps the default hundred and every
# process gets a pool bigger than its share: the bouncer queues what the
# server cannot take, where a bare server refuses it (53300).
if [ -n "$pgbouncer" ]; then pg_max_connections=${FLEET_PG_MAX_CONNECTIONS:-100}; else pg_max_connections=${FLEET_PG_MAX_CONNECTIONS:-300}; fi
docker run -d --name fleet-pg --network fleet-net --cpus "$pg_cpus" -p "$pg_port:5432" -e POSTGRES_PASSWORD=owner -e POSTGRES_DB=premise "$PREMISE_POSTGRES_IMAGE" \
  -c "max_connections=$pg_max_connections" -c shared_preload_libraries=pg_stat_statements -c pg_stat_statements.track=all >/dev/null
for _ in $(seq 1 60); do docker exec fleet-pg pg_isready -h 127.0.0.1 -U postgres >/dev/null 2>&1 && break; sleep 1; done
docker exec fleet-pg psql -U postgres -d premise -q -c "CREATE EXTENSION IF NOT EXISTS pg_stat_statements" >/dev/null

cd "$root"
dotnet build src/Premise.Api -c Release -nologo -v q
export ASPNETCORE_ENVIRONMENT=Development ConnectionStrings__premise="$owner_cs" Secrets__LocalMasterKey="AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA="
ROLE=migrate ASPNETCORE_URLS="http://127.0.0.1:0" dotnet run --project src/Premise.Api -c Release --no-build --no-launch-profile > "$log_dir/migrate.log" 2>&1
if [ -n "$pgbouncer" ]; then
  # the bouncer owns the server budget; each process asks for more than its
  # share on purpose, which is exactly the situation a bare server refuses
  docker run -d --name fleet-pgbouncer --network fleet-net -p "$bouncer_port:5432" \
    -e DB_HOST=fleet-pg -e DB_PORT=5432 -e DB_USER=postgres -e DB_PASSWORD=owner -e DB_NAME=premise \
    -e AUTH_TYPE=scram-sha-256 -e AUTH_USER=postgres -e "AUTH_QUERY=SELECT usename, passwd FROM pg_shadow WHERE usename=\$1" \
    -e POOL_MODE="$pgbouncer" -e MAX_CLIENT_CONN=2000 -e DEFAULT_POOL_SIZE=$((pg_max_connections - 20)) -e MAX_DB_CONNECTIONS=$((pg_max_connections - 20)) \
    -e IGNORE_STARTUP_PARAMETERS=extra_float_digits,search_path -e MAX_PREPARED_STATEMENTS=200 \
    "${FLEET_PGBOUNCER_IMAGE:-edoburu/pgbouncer:v1.24.1-p1}" > "$log_dir/pgbouncer.log" 2>&1
  for _ in $(seq 1 30); do (exec 3<>/dev/tcp/127.0.0.1/$bouncer_port) 2>/dev/null && break; sleep 1; done
  # session mode hands a server connection to a client for as long as the
  # client keeps it, so idle client connections must go back quickly or the
  # first processes to fill their pools starve the rest (the workers never
  # became ready with Npgsql's default five minutes)
  app_cs="Host=localhost;Port=$bouncer_port;Database=premise;Username=postgres;Password=owner;Connection Idle Lifetime=5;Connection Pruning Interval=1"
  pool_size=100
  # the message store bypasses the bouncer (ADR 53): advisory locks and node
  # agents are session state. (A hyphen in the name: env, not export.)
  app_env=("ConnectionStrings__premise-messaging=$owner_cs;Maximum Pool Size=10")
  echo "pgbouncer: $pgbouncer mode, server budget $((pg_max_connections - 20)), each process asks for $pool_size"
else
  app_env=("PREMISE_FLEET=direct") # a non-empty array: bash 3 treats an empty one as unbound under set -u
  app_cs="$owner_cs"
  pool_size=$(( (pg_max_connections - 20) / (2 * replicas) ))
  [ "$pool_size" -gt 40 ] && pool_size=40
fi
export ConnectionStrings__premise="$app_cs;Maximum Pool Size=$pool_size"
export Auth__Provider=local Database__AppUser=app_user Database__AppPassword=app_user
export Storage__LocalRoot="${TMPDIR:-/tmp}/premise-fleet-store" Logging__LogLevel__Default=Warning
# the proxy is the request host the api sees, so CSRF's origin check and every URL it builds agree
export Proxy__TrustForwardedHeaders=true
# the bench measures the api, not the quotas: per-principal limits off (the org quota is raised by fleet-bench.mjs)
if [ "$mode" = bench ]; then export RateLimits__UserPerMinute=100000000 RateLimits__GuestPerMinute=100000000; fi

wait_ready() { # name port
  for _ in $(seq 1 120); do curl -fsS "http://127.0.0.1:$2/healthz" 2>/dev/null | grep -q '"status":"ok"' && return 0; sleep 1; done
  echo "$1 never became ready"; tail -40 "$log_dir/$1.log"; exit 1
}
upstreams=()
api_pids=()
for i in $(seq 1 "$replicas"); do
  port=$((api_base + i - 1))
  # --no-launch-profile: launchSettings.json would pin every replica to the same port
  env "${app_env[@]}" ROLE=api ASPNETCORE_URLS="http://127.0.0.1:$port" dotnet run --project src/Premise.Api -c Release --no-build --no-launch-profile > "$log_dir/api-$i.log" 2>&1 &
  pids+=($!); api_pids+=($!)
  # the first replica seeds the dev data before the others boot (the seed is idempotent, not concurrent)
  wait_ready "api-$i" "$port"
  upstreams+=("http://127.0.0.1:$port")
done
worker_pids=()
for i in $(seq 1 "$replicas"); do
  port=$((worker_base + i - 1))
  env "${app_env[@]}" ROLE=worker ASPNETCORE_URLS="http://127.0.0.1:$port" dotnet run --project src/Premise.Api -c Release --no-build --no-launch-profile > "$log_dir/worker-$i.log" 2>&1 &
  pids+=($!); worker_pids+=($!)
done
for i in $(seq 1 "$replicas"); do wait_ready "worker-$i" $((worker_base + i - 1)); done
node "$root/tools/replica-proxy.mjs" "$proxy_port" "${upstreams[@]}" > "$log_dir/proxy.log" 2>&1 &
pids+=($!)
for _ in $(seq 1 30); do curl -fsS "http://127.0.0.1:$proxy_port/healthz" >/dev/null 2>&1 && break; sleep 1; done
echo "fleet: $replicas api + $replicas worker behind http://127.0.0.1:$proxy_port"

if [ "$mode" = bench ]; then
  PREMISE_PG_STATS_CMD="docker exec fleet-pg psql -U postgres -d premise -tA -c" \
  node "$root/tools/fleet-bench.mjs" "http://127.0.0.1:$proxy_port" "$seconds" "$concurrency" "$replicas${pgbouncer:+ (pgbouncer $pgbouncer)}"
else
  PREMISE_FLEET_URL="http://127.0.0.1:$proxy_port" \
  PREMISE_FLEET_PG="$owner_cs" \
  PREMISE_FLEET_REPLICAS="$replicas" \
  PREMISE_FLEET_WORKER_PORTS="$(seq -s, "$worker_base" $((worker_base + replicas - 1)))" \
  dotnet test tests/Premise.FleetTests -c Release --logger "console;verbosity=normal"
fi
