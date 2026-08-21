#!/usr/bin/env zsh
set -euo pipefail

ROOT=${0:A:h:h}
PORT=${PORT:-5082}
LOG="$ROOT/.demo-workspaces/checkout-api.log"

mkdir -p "$ROOT/.demo-workspaces"
dotnet run --project "$ROOT/src/Checkout.Api/Checkout.Api.csproj" \
  -c Release --no-build --urls "http://127.0.0.1:$PORT" >"$LOG" 2>&1 &
pid=$!

cleanup() {
  if kill -0 "$pid" 2>/dev/null; then
    kill "$pid"
    wait "$pid" 2>/dev/null || true
  fi
}
trap cleanup EXIT INT TERM

for _ in {1..30}; do
  if curl --fail --silent "http://127.0.0.1:$PORT/health" >/dev/null; then
    break
  fi
  sleep 0.2
done

curl --fail --silent "http://127.0.0.1:$PORT/health"
print
curl --fail --silent \
  -H "Content-Type: application/json" \
  -H "X-Client-Id: smoke-client" \
  -d '{"items":[{"quantity":2,"unitPrice":12.5}],"discountPercent":10}' \
  "http://127.0.0.1:$PORT/checkout"
print
