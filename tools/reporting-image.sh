#!/usr/bin/env bash
# Qualify actual reference jobs from the OCI image in Production. Local-only
# fixtures bootstrap a session; no external auth, basemap or paid service is used.
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
image="${1:?usage: bash tools/reporting-image.sh IMAGE [OUTPUT]}"
platform="$(docker image inspect "$image" --format '{{.Os}}/{{.Architecture}}')"
output="${2:-$(mktemp -d "${TMPDIR:-/tmp}/premise-reporting-image.XXXXXX")}"
mkdir -p "$output"
output="$(cd "$output" && pwd)"
chmod 700 "$output"
prefix="premise-report-image-$$"
net="$prefix-net"; keys="$prefix-keys"
containers=()
cleanup() {
  local status=$?
  set +e
  for ((i=${#containers[@]}-1; i>=0; i--)); do
    name="${containers[i]}"
    docker logs "$name" > "$output/$name.log" 2>&1
    docker rm -f "$name" >/dev/null
  done
  docker volume rm "$keys" >/dev/null 2>&1
  docker network rm "$net" >/dev/null 2>&1
  rm -f "$output/session.cookies"
  echo "Reporting image evidence: $output"
  return "$status"
}
trap cleanup EXIT
docker network create "$net" >/dev/null
docker volume create "$keys" >/dev/null
docker run --rm --platform "$platform" --user root -v "$keys:/keys" --entrypoint sh "$image" -c 'chown app:app /keys'
docker image inspect "$image" --format '{{json .}}' > "$output/image.json"
source "$root/tools/postgres-image.sh"
containers+=("$prefix-pg")
docker run -d --name "$prefix-pg" --network "$net" -e POSTGRES_PASSWORD=owner -e POSTGRES_DB=premise "$PREMISE_POSTGRES_IMAGE" >/dev/null
for _ in $(seq 1 60); do docker exec "$prefix-pg" pg_isready -h 127.0.0.1 -U postgres >/dev/null 2>&1 && break; sleep 1; done
containers+=("$prefix-s3")
docker run -d --name "$prefix-s3" --network "$net" --network-alias report-s3 -p 127.0.0.1::9000 \
  -e MINIO_ROOT_USER=fixture-access -e MINIO_ROOT_PASSWORD=fixture-secret minio/minio:latest server /data >/dev/null
s3_port="$(docker port "$prefix-s3" 9000/tcp | cut -d: -f2)"
for _ in $(seq 1 60); do curl -fsS "http://127.0.0.1:$s3_port/minio/health/ready" >/dev/null 2>&1 && break; sleep 1; done
curl -fsS --aws-sigv4 'aws:amz:us-east-1:s3' --user fixture-access:fixture-secret -X PUT "http://127.0.0.1:$s3_port/reports"
common=(--platform "$platform" --network "$net" --read-only --memory 768m --cpus 1 --pids-limit 256 --ulimit core=0
  --tmpfs /tmp:rw,nosuid,size=256m -v "$keys:/keys"
  -e "ConnectionStrings__premise=Host=$prefix-pg;Database=premise;Username=postgres;Password=owner;Maximum Pool Size=20"
  -e Database__AppUser=app_user -e Database__AppPassword=app_user -e DataProtection__KeyPath=/keys
  -e Storage__Provider=s3 -e Storage__S3__BucketName=reports -e Storage__S3__ServiceUrl=http://report-s3:9000
  -e Storage__S3__AccessKey=fixture-access -e Storage__S3__SecretKey=fixture-secret -e Storage__S3__ForcePathStyle=true)
production=(-e ASPNETCORE_ENVIRONMENT=Production
  -e Auth__Provider=workos -e Auth__WorkOS__ApiKey=sk_unused -e Auth__WorkOS__ClientId=client_unused
  -e Scanner__Provider=clamav -e Scanner__ClamAv__Host=unused
  -e Secrets__Provider=kms -e Secrets__Kms__KeyId=unused
  -e Billing__Provider=stripe -e Billing__Stripe__ApiKey=sk_unused -e Billing__Stripe__WebhookSecret=whsec_unused
  -e Billing__Stripe__PriceIds__growth=price_unused -e Billing__Stripe__PriceIds__scale=price_unused
  -e Notifications__Transport=smtp -e Notifications__Smtp__Host=unused -e Notifications__Smtp__FromAddress=noreply@example.test
  -e Public__HostTemplate=https://{slug}.example.test)
docker run --rm "${common[@]}" "${production[@]}" -e ROLE=migrate "$image" > "$output/migrate.log" 2>&1
wait_ready() {
  local name=$1 port=$2
  for _ in $(seq 1 120); do
    [ "$(docker inspect -f '{{.State.Running}}' "$name")" = true ] || { docker logs "$name"; return 1; }
    if curl -fsS "http://127.0.0.1:$port/healthz" > "$output/$name-health.json" 2>/dev/null; then return; fi
    sleep 1
  done
  docker logs "$name"; return 1
}
# Only fixture setup uses Development. The same image then restarts as Production.
containers+=("$prefix-api")
docker run -d --name "$prefix-api" "${common[@]}" -p 127.0.0.1::8080 -e ROLE=api \
  -e ASPNETCORE_ENVIRONMENT=Development -e Auth__Provider=local \
  -e Secrets__LocalMasterKey=AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA= "$image" >/dev/null
api_port="$(docker port "$prefix-api" 8080/tcp | cut -d: -f2)"
wait_ready "$prefix-api" "$api_port"
python3 "$root/tools/reporting-image.py" prepare "http://127.0.0.1:$api_port" "$s3_port" "$output"
docker logs "$prefix-api" > "$output/bootstrap.log" 2>&1
docker rm -f "$prefix-api" >/dev/null
docker run -d --name "$prefix-api" "${common[@]}" "${production[@]}" -p "127.0.0.1:$api_port:8080" -e ROLE=api "$image" >/dev/null
wait_ready "$prefix-api" "$api_port"
containers+=("$prefix-worker")
docker run -d --name "$prefix-worker" "${common[@]}" "${production[@]}" -p 127.0.0.1::8080 -e ROLE=worker "$image" >/dev/null
worker_port="$(docker port "$prefix-worker" 8080/tcp | cut -d: -f2)"
wait_ready "$prefix-worker" "$worker_port"
for name in "$prefix-api" "$prefix-worker"; do
  [ "$(docker exec "$name" id -u)" != 0 ] || { echo 'root process refused'; exit 1; }
done
docker exec "$prefix-pg" psql -X -v ON_ERROR_STOP=1 -U postgres -d premise -Atc \
  "SELECT EXISTS (SELECT FROM pg_stat_activity WHERE usename = 'app_user')
     AND NOT EXISTS (SELECT FROM pg_stat_activity WHERE usename = 'postgres' AND pid <> pg_backend_pid() AND backend_type = 'client backend')" \
  | tee "$output/application-role-check.txt" | grep -qx t
python3 "$root/tools/reporting-image.py" verify "http://127.0.0.1:$api_port" "$s3_port" "$output"
docker exec "$prefix-pg" psql -X -v ON_ERROR_STOP=1 -U postgres -d premise -Atc \
  "SELECT count(*) FROM wolverine.wolverine_dead_letters" | tee "$output/dead-letter-count.txt" | grep -qx 0
echo 'Production image: single, aggregate, 100-site ZIP, site-file publication and quota settlement passed.'
