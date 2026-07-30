#!/usr/bin/env bash
# T008 — verify PDF carve-outs are byte-identical to main (FR-020, SC-014).

set -u

REPO_ROOT="$(git -C "$(dirname "$0")" rev-parse --show-toplevel 2>/dev/null || pwd)"
cd "$REPO_ROOT" || exit 2

CARVEOUT1="src/FundingPlatform.Web/Views/FundingAgreement/Document.cshtml"
CARVEOUT2="src/FundingPlatform.Web/Views/FundingAgreement/_FundingAgreementLayout.cshtml"

violations=0
for f in "$CARVEOUT1" "$CARVEOUT2"; do
  diff_out=$(git diff main -- "$f" 2>/dev/null || true)
  if [ -n "$diff_out" ]; then
    echo "FAIL: $f differs from main"
    echo "$diff_out" | head -20
    violations=$((violations + 1))
  else
    echo "OK:   $f unchanged from main"
  fi
done

if [ "$violations" -gt 0 ]; then
  echo "verify-pdf-carveouts: $violations carve-out(s) drifted"
  exit 1
fi
echo "verify-pdf-carveouts: all carve-outs preserved"
