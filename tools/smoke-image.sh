#!/usr/bin/env bash
# Boot the three roles from ONE image in Production mode against a real
# PostgreSQL, the way an orchestrator would: migrate runs to completion with
# owner credentials, then api and worker start as app_user and must answer
# both probes. Every adapter seam is on a production provider (registration
# is lazy, nothing is contacted), so this is also the boot guards' negative
# control. Usage: tools/smoke-image.sh <image>
set -euo pipefail
image="${1:?image}"
net="premise-smoke-$$"
cleanup() { docker rm -f smoke-pg smoke-api smoke-worker >/dev/null 2>&1 || true; docker network rm "$net" >/dev/null 2>&1 || true; }
trap cleanup EXIT
docker network create "$net" >/dev/null
docker run -d --name smoke-pg --network "$net" -e POSTGRES_PASSWORD=owner -e POSTGRES_DB=premise postgres:17-alpine >/dev/null
for _ in $(seq 1 30); do docker exec smoke-pg pg_isready -U postgres >/dev/null 2>&1 && break; sleep 1; done

common=(
  --network "$net"
  -e ASPNETCORE_ENVIRONMENT=Production
  -e "ConnectionStrings__premise=Host=smoke-pg;Database=premise;Username=postgres;Password=owner"
  -e Database__AppUser=app_user -e Database__AppPassword=app_user
  -e DataProtection__KeyPath=/tmp/keys
  -e Auth__Provider=workos -e Auth__WorkOS__ApiKey=sk_unused -e Auth__WorkOS__ClientId=client_unused
  -e Storage__Provider=s3 -e Storage__S3__BucketName=unused
  -e Scanner__Provider=clamav -e Scanner__ClamAv__Host=unused
  -e Secrets__Provider=kms -e Secrets__Kms__KeyId=unused
  -e Billing__Provider=stripe -e Billing__Stripe__SecretKey=sk_unused -e Billing__Stripe__WebhookSecret=whsec_unused
  -e Notifications__Transport=smtp -e Notifications__Smtp__Host=unused
  -e Public__HostTemplate=https://{slug}.example.test
  -e Build__Version=smoke
)

echo "== migrate"
docker run --rm "${common[@]}" -e ROLE=migrate "$image"

probe() { # name port
  local name=$1 port=$2
  for _ in $(seq 1 90); do
    curl -fsS "http://localhost:$port/livez" >/dev/null 2>&1 && break
    sleep 1
  done
  curl -fsS "http://localhost:$port/livez" | grep -q '"status":"alive"' || { echo "$name: no liveness"; docker logs "$name"; exit 1; }
  for _ in $(seq 1 60); do
    curl -fsS "http://localhost:$port/healthz" 2>/dev/null | grep -q '"status":"ok"' && break
    sleep 1
  done
  curl -fsS "http://localhost:$port/healthz" | grep -q "\"role\":\"${name#smoke-}\"" || { echo "$name: not ready"; docker logs "$name"; exit 1; }
  echo "$name: alive and ready"
}
echo "== api"
docker run -d --name smoke-api "${common[@]}" -p 18080:8080 -e ROLE=api "$image" >/dev/null
echo "== worker"
docker run -d --name smoke-worker "${common[@]}" -p 18081:8080 -e ROLE=worker "$image" >/dev/null
probe smoke-api 18080
probe smoke-worker 18081
docker logs smoke-api 2>&1 | grep -qi "exception" && { echo "api logged an exception"; docker logs smoke-api; exit 1; }
echo "all three roles booted from $image"
