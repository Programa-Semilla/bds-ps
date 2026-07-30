#!/usr/bin/env bash
# T007 — verify each SVG under wwwroot/lib/illustrations/ is ≤ 8 KB gzipped (SC-007).

set -u

REPO_ROOT="$(git -C "$(dirname "$0")" rev-parse --show-toplevel 2>/dev/null || pwd)"
cd "$REPO_ROOT" || exit 2

DIR="src/FundingPlatform.Web/wwwroot/lib/illustrations"
LIMIT_BYTES=$((8 * 1024))
violations=0

if [ ! -d "$DIR" ]; then
  echo "verify-illustrations: $DIR does not exist"
  exit 1
fi

for svg in "$DIR"/*.svg; do
  [ -f "$svg" ] || continue
  size=$(gzip -c "$svg" | wc -c)
  if [ "$size" -gt "$LIMIT_BYTES" ]; then
    echo "FAIL: $svg gzipped=${size}B exceeds ${LIMIT_BYTES}B"
    violations=$((violations + 1))
  else
    echo "OK:   $svg gzipped=${size}B"
  fi
done

if [ "$violations" -gt 0 ]; then
  echo "verify-illustrations: $violations file(s) failed"
  exit 1
fi
echo "verify-illustrations: all SVGs within budget"
