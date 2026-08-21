#!/usr/bin/env zsh
set -euo pipefail

readonly LEGACY_BASE_URL="${1:-http://localhost:5001}"
readonly EDGE_BASE_URL="${2:-http://localhost:5002}"

for tool in curl jq; do
  command -v "${tool}" >/dev/null 2>&1 || {
    print -u2 "ERROR: ${tool} is required for smoke validation."
    exit 1
  }
done

wait_for_api() {
  local base_url="$1"
  local attempt
  for attempt in {1..60}; do
    if curl --fail --silent --show-error --max-time 2 \
      "${base_url}/api/orders" >/dev/null 2>&1; then
      return
    fi
    sleep 2
  done
  print -u2 "ERROR: API did not become ready: ${base_url}"
  return 1
}

readonly TEMP_DIR="$(mktemp -d)"
trap 'rm -rf "${TEMP_DIR}"' EXIT

wait_for_api "${LEGACY_BASE_URL}"
wait_for_api "${EDGE_BASE_URL}"

curl --fail --silent --show-error "${LEGACY_BASE_URL}/api/orders" \
  | jq 'map({customerId, sku, quantity, status})' >"${TEMP_DIR}/legacy-seed.json"
curl --fail --silent --show-error "${EDGE_BASE_URL}/api/orders" \
  | jq 'map({customerId, sku, quantity, status})' >"${TEMP_DIR}/edge-seed.json"
diff -u "${TEMP_DIR}/legacy-seed.json" "${TEMP_DIR}/edge-seed.json" >/dev/null || {
  print -u2 "ERROR: Seeded public order contracts differ."
  diff -u "${TEMP_DIR}/legacy-seed.json" "${TEMP_DIR}/edge-seed.json" >&2 || true
  exit 1
}

readonly ORDER_REQUEST='{"customerId":"CUST-SMOKE","sku":"BLUE-LAMP","quantity":1}'

post_order() {
  local base_url="$1"
  local output="$2"
  local status
  status="$(curl --silent --show-error --output "${output}" --write-out '%{http_code}' \
    --header 'Content-Type: application/json' \
    --data "${ORDER_REQUEST}" \
    "${base_url}/api/orders")"
  [[ "${status}" == "201" ]] || {
    print -u2 "ERROR: Expected 201 from ${base_url}/api/orders; received ${status}."
    cat "${output}" >&2
    return 1
  }
}

post_order "${LEGACY_BASE_URL}" "${TEMP_DIR}/legacy-order.json"
post_order "${EDGE_BASE_URL}" "${TEMP_DIR}/edge-order.json"

readonly LEGACY_ORDER_ID="$(jq -er '.id' "${TEMP_DIR}/legacy-order.json")"
readonly EDGE_ORDER_ID="$(jq -er '.id' "${TEMP_DIR}/edge-order.json")"

curl --fail --silent --show-error --request POST \
  "${LEGACY_BASE_URL}/api/orders/${LEGACY_ORDER_ID}/fulfill" \
  >"${TEMP_DIR}/legacy-fulfilled.json"
curl --fail --silent --show-error --request POST \
  "${EDGE_BASE_URL}/api/orders/${EDGE_ORDER_ID}/fulfill" \
  >"${TEMP_DIR}/edge-fulfilled.json"

[[ "$(jq -r '.status' "${TEMP_DIR}/legacy-fulfilled.json")" == "Fulfilled" ]]
[[ "$(jq -r '.status' "${TEMP_DIR}/edge-fulfilled.json")" == "Fulfilled" ]]

readonly LEGACY_AVAILABLE="$(curl --fail --silent --show-error \
  "${LEGACY_BASE_URL}/api/inventory/BLUE-LAMP" | jq -er '.available')"
readonly EDGE_AVAILABLE="$(curl --fail --silent --show-error \
  "${EDGE_BASE_URL}/api/inventory/BLUE-LAMP" | jq -er '.available')"

[[ "${LEGACY_AVAILABLE}" == "${EDGE_AVAILABLE}" ]] || {
  print -u2 "ERROR: Inventory parity failed (${LEGACY_AVAILABLE} != ${EDGE_AVAILABLE})."
  exit 1
}

print "Smoke test passed: seeded contract parity, order creation, cross-service fulfillment,"
print "and inventory parity were verified. BLUE-LAMP available: ${EDGE_AVAILABLE}."
