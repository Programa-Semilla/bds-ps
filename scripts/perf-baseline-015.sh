#!/usr/bin/env bash
# Spec 015 / T906 — perf baseline for multi-currency endpoints.
#
# Measures p95 latency of three hot paths added by spec 015 against a running
# AppHost and reports current numbers.
#
#   - POST /Application/{appId}/Item/{itemId}/Quotation/Convert  (target ≤ 200 ms)
#   - POST /Application/{appId}/Item/{itemId}/Quotation/Add (USD save) (target ≤ 800 ms)
#   - POST /Admin/AdminExchangeRates                              (target ≤ 500 ms)
#
# Usage:
#
#   AUTH_COOKIE="aspnet=...; admin=..." \
#   APP_ID=42 ITEM_ID=99 BASE_URL=http://localhost:5000 \
#   ./scripts/perf-baseline-015.sh
#
# All env vars except BASE_URL default to placeholder values that will fail
# fast with a 4xx; the script focuses on shape, not authentication.
#
# Exit codes:
#   0 — every endpoint within 2x of the embedded baseline (or no baseline yet)
#   1 — at least one endpoint regressed by more than 2x vs. baseline
#   2 — invocation error (curl missing, etc.)
#
# This script is intentionally NOT executed from CI — it's a developer tool for
# capturing comparable numbers when a stable AppHost is available. The thresholds
# above are advisory non-blocking goals from spec 015 plan.md.

set -u

if ! command -v curl >/dev/null 2>&1; then
  echo "perf-baseline-015: 'curl' is required" >&2
  exit 2
fi

BASE_URL="${BASE_URL:-http://localhost:5000}"
APP_ID="${APP_ID:-1}"
ITEM_ID="${ITEM_ID:-1}"
AUTH_COOKIE="${AUTH_COOKIE:-}"
ITERATIONS="${ITERATIONS:-20}"

# Embedded baseline (milliseconds, p95). Update by re-running this script
# on a quiet machine with a primed AppHost. Regression gate triggers at >2x.
BASELINE_CONVERT_MS=200
BASELINE_SAVE_USD_MS=800
BASELINE_RATE_PUBLISH_MS=500

# ----- helpers --------------------------------------------------------------

# Print the p95 (in ms) of a list of curl '%{time_total}' values (in seconds).
p95() {
  awk '{ print $1 * 1000 }' \
    | sort -n \
    | awk -v n="$ITERATIONS" '
        BEGIN { idx = int(0.95 * n); if (idx < 1) idx = 1 }
        { v[NR] = $1 }
        END   { printf "%.1f", v[idx] }'
}

# Time a single request, emitting only the time_total in seconds.
time_request() {
  local method="$1" url="$2" body="$3"
  local args=(-s -o /dev/null -w '%{time_total}\n' -X "$method" "$url"
              -H 'Content-Type: application/json'
              -H 'Accept: application/json')
  if [[ -n "$AUTH_COOKIE" ]]; then
    args+=(-H "Cookie: $AUTH_COOKIE")
  fi
  if [[ -n "$body" ]]; then
    args+=(--data "$body")
  fi
  curl "${args[@]}" || echo "0"
}

# Run a request N times and print "p95_ms current_ms baseline_ms gate"
benchmark() {
  local label="$1" baseline="$2" method="$3" url="$4" body="$5"
  local samples
  samples="$(for _ in $(seq 1 "$ITERATIONS"); do
              time_request "$method" "$url" "$body"
            done)"
  local current
  current="$(echo "$samples" | p95)"
  local gate=$(awk -v c="$current" -v b="$baseline" 'BEGIN { print (c > 2*b) ? "REGRESSED" : "OK" }')
  printf "%-30s p95=%8.1fms baseline=%5dms iterations=%d gate=%s\n" \
         "$label" "$current" "$baseline" "$ITERATIONS" "$gate"
  if [[ "$gate" == "REGRESSED" ]]; then return 1; fi
  return 0
}

# ----- benchmarks -----------------------------------------------------------

echo "perf-baseline-015 against $BASE_URL (APP_ID=$APP_ID ITEM_ID=$ITEM_ID, iterations=$ITERATIONS)"

regressed=0

CONVERT_URL="$BASE_URL/Application/$APP_ID/Item/$ITEM_ID/Quotation/Convert"
CONVERT_BODY='{"currencyCode":"USD","amount":1000.00}'
benchmark "POST Quotation/Convert"            "$BASELINE_CONVERT_MS" \
  POST "$CONVERT_URL" "$CONVERT_BODY" || regressed=1

SAVE_URL="$BASE_URL/Application/$APP_ID/Item/$ITEM_ID/Quotation/Add"
SAVE_BODY='{"price":1000.00,"currency":"USD","supplierId":1,"validUntil":"2027-12-31"}'
benchmark "POST Quotation/Add (USD save)"     "$BASELINE_SAVE_USD_MS" \
  POST "$SAVE_URL"    "$SAVE_BODY"    || regressed=1

RATE_URL="$BASE_URL/Admin/AdminExchangeRates"
RATE_BODY='{"sourceCurrencyCode":"USD","targetCurrencyCode":"CRC","buyRate":520,"sellRate":525,"effectiveAtLocal":"2026-05-01T00:00"}'
benchmark "POST AdminExchangeRates"           "$BASELINE_RATE_PUBLISH_MS" \
  POST "$RATE_URL"    "$RATE_BODY"    || regressed=1

if (( regressed != 0 )); then
  echo "perf-baseline-015: at least one endpoint regressed >2x vs. baseline" >&2
  exit 1
fi
exit 0
