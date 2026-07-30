#!/usr/bin/env bash
# T009 — aggregate gate: tokens + illustrations + PDF carve-outs.

set -u

REPO_ROOT="$(git -C "$(dirname "$0")" rev-parse --show-toplevel 2>/dev/null || pwd)"
cd "$REPO_ROOT" || exit 2

failures=0

echo "=== verify-tokens ==="
bash scripts/verify-tokens.sh || failures=$((failures + 1))

echo
echo "=== verify-illustrations ==="
bash scripts/verify-illustrations.sh || failures=$((failures + 1))

echo
echo "=== verify-pdf-carveouts ==="
bash scripts/verify-pdf-carveouts.sh || failures=$((failures + 1))

echo
echo "=== verify-asset-budget ==="
bash scripts/verify-asset-budget.sh || failures=$((failures + 1))

if [ "$failures" -gt 0 ]; then
  echo "verify-facelift: $failures gate(s) failed"
  exit 1
fi
echo "verify-facelift: all gates passed"
