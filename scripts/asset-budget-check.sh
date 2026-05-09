#!/usr/bin/env bash
# Spec 019 T032 — asset budget check (FR-037 / NFR-002 / SC-011).
# Enumerates the new asset paths and asserts combined wire weight ≤ 400 KB gz.

set -u

REPO_ROOT="$(git -C "$(dirname "$0")" rev-parse --show-toplevel 2>/dev/null || pwd)"
cd "$REPO_ROOT" || exit 2

LIMIT_BYTES=$((400 * 1024))
total=0

scan_dir() {
  local label="$1"; shift
  local pattern="$1"; shift
  echo "$label:"
  # shellcheck disable=SC2086
  for f in $pattern; do
    [ -f "$f" ] || continue
    size=$(gzip -c "$f" | wc -c)
    total=$((total + size))
    echo "  $f gzipped=${size}B"
  done
}

scan_dir "Fonts (Inter + JetBrains Mono — Fraunces removed by FR-013)" \
  "src/FundingPlatform.Web/wwwroot/lib/fonts/*/*.woff2"

scan_dir "Empty-state illustrations (9 SVGs, re-stroked teal by FR-026)" \
  "src/FundingPlatform.Web/wwwroot/lib/illustrations/*.svg"

scan_dir "Canvas confetti" \
  "src/FundingPlatform.Web/wwwroot/lib/canvas-confetti/*.js"

scan_dir "Brand assets (mark / wordmark / seal)" \
  "src/FundingPlatform.Web/wwwroot/lib/brand/*.svg"

scan_dir "Sponsor partner logos (5 SVGs — FR-003)" \
  "src/FundingPlatform.Web/wwwroot/lib/brand/sponsors/*.svg"

scan_dir "Favicons" \
  "src/FundingPlatform.Web/wwwroot/lib/brand/favicons/*"

echo ""
echo "Total brand wire weight: $((total / 1024)) KB gz (limit $((LIMIT_BYTES / 1024)) KB)"
if [ "$total" -gt "$LIMIT_BYTES" ]; then
  echo "FAIL: asset budget exceeded"
  exit 1
fi
echo "OK"
