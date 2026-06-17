#!/usr/bin/env bash
# T127 — verify combined wire weight of fonts + 9 SVGs + canvas-confetti + brand assets ≤ 400 KB gz (SC-016, FR-074).

set -u

REPO_ROOT="$(git -C "$(dirname "$0")" rev-parse --show-toplevel 2>/dev/null || pwd)"
cd "$REPO_ROOT" || exit 2

LIMIT_BYTES=$((400 * 1024))
total=0

echo "Fonts:"
for f in src/FundingPlatform.Web/wwwroot/lib/fonts/**/*.woff2; do
  [ -f "$f" ] || continue
  size=$(gzip -c "$f" | wc -c)
  total=$((total + size))
  echo "  $f gzipped=${size}B"
done

echo "Illustrations:"
for f in src/FundingPlatform.Web/wwwroot/lib/illustrations/*.svg; do
  [ -f "$f" ] || continue
  size=$(gzip -c "$f" | wc -c)
  total=$((total + size))
  echo "  $f gzipped=${size}B"
done

echo "Canvas confetti:"
for f in src/FundingPlatform.Web/wwwroot/lib/canvas-confetti/*.js; do
  [ -f "$f" ] || continue
  size=$(gzip -c "$f" | wc -c)
  total=$((total + size))
  echo "  $f gzipped=${size}B"
done

echo "Brand assets:"
for f in src/FundingPlatform.Web/wwwroot/lib/brand/*.svg src/FundingPlatform.Web/wwwroot/lib/brand/*.png src/FundingPlatform.Web/wwwroot/lib/brand/favicons/*; do
  [ -f "$f" ] || continue
  size=$(gzip -c "$f" | wc -c)
  total=$((total + size))
  echo "  $f gzipped=${size}B"
done

echo "Total gzipped: ${total}B (limit ${LIMIT_BYTES}B)"
if [ "$total" -gt "$LIMIT_BYTES" ]; then
  echo "FAIL: asset budget exceeded"
  exit 1
fi
echo "OK"
