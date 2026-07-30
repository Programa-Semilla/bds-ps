#!/usr/bin/env bash
# Spec 019 T031 — tokens audit (extends spec 011 verify-tokens.sh).
# Asserts tokens.css is the only file with raw hex values, with the spec-011
# carve-outs preserved and wwwroot/lib/brand/pdf/ carved out per FR-039.
# Output: "OK" + exit 0 on success.

set -u

REPO_ROOT="$(git -C "$(dirname "$0")" rev-parse --show-toplevel 2>/dev/null || pwd)"
cd "$REPO_ROOT" || exit 2

WEB_ROOT="src/FundingPlatform.Web"
TOKENS_CSS="$WEB_ROOT/wwwroot/css/tokens.css"
PDF_CARVEOUT_DIR="$WEB_ROOT/wwwroot/lib/brand/pdf"
PDF_DOC_VIEW="$WEB_ROOT/Views/FundingAgreement"

violations=0

echo "Scanning for raw hex values outside tokens.css and PDF carve-outs..."
hex_hits=$(grep -RIn --include='*.css' --include='*.cshtml' --include='*.js' \
  --exclude-dir=obj --exclude-dir=bin --exclude-dir=lib \
  -E '#[0-9a-fA-F]{3}([0-9a-fA-F]{3})?\b' "$WEB_ROOT" 2>/dev/null \
  | grep -v "$TOKENS_CSS" \
  | grep -v "$PDF_CARVEOUT_DIR" \
  | grep -v "$PDF_DOC_VIEW" \
  || true)
if [ -n "$hex_hits" ]; then
  echo "$hex_hits"
  violations=$((violations + 1))
fi

# Brand SVGs (mark/wordmark/seal + sponsors) inline teal hex literals — these are
# carved-out brand assets, not stylesheet-driven UI. The carve-out matches spec 011's
# original allowance for vendored SVG art.
echo "Scanning Razor partials for inline hex (excluding brand SVGs and PDF carve-outs)..."
razor_hex=$(grep -RIn --include='*.cshtml' \
  --exclude-dir=obj --exclude-dir=bin \
  -E '#[0-9a-fA-F]{3}([0-9a-fA-F]{3})?\b' "$WEB_ROOT/Views" 2>/dev/null \
  | grep -v "$PDF_DOC_VIEW" \
  || true)
if [ -n "$razor_hex" ]; then
  echo "$razor_hex"
  violations=$((violations + 1))
fi

if [ "$violations" -gt 0 ]; then
  echo "tokens-audit: FAIL ($violations violation(s))"
  exit 1
fi
echo "OK"
