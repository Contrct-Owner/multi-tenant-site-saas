#!/usr/bin/env bash
# Boot the stack a browser run needs, run the Playwright suite, tear down.
# Postgres in Docker; the migrate role, then the api role in Development with
# the LOCAL auth provider (sign-in by hint, no credentials typed - the same
# password-less boot PREMISE_AUTH=local gives the AppHost); the console dev
# server proxying to it. Usage: tools/e2e-stack.sh [playwright args]
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
pg_port=${E2E_PG_PORT:-55432}; api_port=${E2E_API_PORT:-5293}; web_port=${E2E_WEB_PORT:-5173}
cs="Host=localhost;Port=$pg_port;Database=premise;Username=postgres;Password=owner"
cleanup() {
  [ -n "${api_pid:-}" ] && kill "$api_pid" 2>/dev/null || true
  [ -n "${web_pid:-}" ] && kill "$web_pid" 2>/dev/null || true
  docker rm -f e2e-pg >/dev/null 2>&1 || true
}
trap cleanup EXIT
docker rm -f e2e-pg >/dev/null 2>&1 || true
docker run -d --name e2e-pg -p "$pg_port:5432" -e POSTGRES_PASSWORD=owner -e POSTGRES_DB=premise postgres:17-alpine >/dev/null
for _ in $(seq 1 30); do docker exec e2e-pg pg_isready -U postgres >/dev/null 2>&1 && break; sleep 1; done

cd "$root"
dotnet build src/Premise.Api -c Release -nologo -v q
export ASPNETCORE_ENVIRONMENT=Development ConnectionStrings__premise="$cs"
ROLE=migrate ASPNETCORE_URLS="http://127.0.0.1:0" dotnet run --project src/Premise.Api -c Release --no-build
export ROLE=api Auth__Provider=local Database__AppUser=app_user Database__AppPassword=app_user
export Storage__LocalRoot="${TMPDIR:-/tmp}/premise-e2e-store" Secrets__LocalMasterKey="AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA="
export ASPNETCORE_URLS="http://localhost:$api_port"
dotnet run --project src/Premise.Api -c Release --no-build > "${TMPDIR:-/tmp}/premise-e2e-api.log" 2>&1 &
api_pid=$!
for _ in $(seq 1 120); do curl -fsS "http://localhost:$api_port/healthz" >/dev/null 2>&1 && break; sleep 1; done
curl -fsS "http://localhost:$api_port/healthz" | grep -q '"status":"ok"' || { echo "api never became ready"; tail -50 "${TMPDIR:-/tmp}/premise-e2e-api.log"; exit 1; }

cd "$root/web"
PORT=$web_port PREMISE_API="http://localhost:$api_port" pnpm --filter @premise/console dev > "${TMPDIR:-/tmp}/premise-e2e-web.log" 2>&1 &
web_pid=$!
for _ in $(seq 1 60); do curl -fsS "http://localhost:$web_port/" >/dev/null 2>&1 && break; sleep 1; done
E2E_CONSOLE="http://localhost:$web_port" pnpm --filter @premise/e2e test "$@"
