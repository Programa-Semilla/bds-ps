#!/usr/bin/env bash
# T006 — verify token discipline (SC-001, SC-002, SC-003, FR-070).
#
# Fails non-zero on any violation. Greps:
#   1. Raw hex outside tokens.css and PDF carve-outs (FR-009, SC-001).
#   2. Inline style= in non-PDF Razor views (FR-010, SC-002).
#   3. Hard-coded animation durations outside tokens.css (FR-014, SC-003).
#   4. Value-bound token names like --color-white or --motion-200ms (FR-070, research §3).
#
# Each violation prints the file:line that triggered it.

set -u

REPO_ROOT="$(git -C "$(dirname "$0")" rev-parse --show-toplevel 2>/dev/null || pwd)"
cd "$REPO_ROOT" || exit 2

WEB_ROOT="src/FundingPlatform.Web"
TOKENS_CSS="$WEB_ROOT/wwwroot/css/tokens.css"
CARVEOUT_DOC="$WEB_ROOT/Views/FundingAgreement/Document.cshtml"
CARVEOUT_LAYOUT="$WEB_ROOT/Views/FundingAgreement/_FundingAgreementLayout.cshtml"

violations=0

echo "[1/4] Raw-hex outside tokens.css and PDF carve-outs..."
# Scan source-controlled css + cshtml + js; allow PDF carve-outs, tokens.css,
# vendored libs, and exclude build artifacts under obj/ and bin/. The .js scan
# catches color tokens that JS produces at runtime (FR-009 spirit).
hex_hits=$(grep -RIn --include='*.css' --include='*.cshtml' --include='*.js' \
  --exclude-dir=obj --exclude-dir=bin --exclude-dir=lib \
  -E '#[0-9a-fA-F]{3}([0-9a-fA-F]{3})?\b' "$WEB_ROOT" 2>/dev/null \
  | grep -v "$TOKENS_CSS" \
  | grep -v "$CARVEOUT_DOC" \
  | grep -v "$CARVEOUT_LAYOUT" \
  || true)
if [ -n "$hex_hits" ]; then
  echo "$hex_hits"
  violations=$((violations + 1))
fi

echo "[2/4] Inline style= attributes in views (excluding PDF carve-outs)..."
inline_hits=$(grep -RIn --include='*.cshtml' \
  --exclude-dir=obj --exclude-dir=bin \
  -E 'style="[^"]+"' "$WEB_ROOT/Views" 2>/dev/null \
  | grep -v "Document.cshtml" \
  | grep -v "_FundingAgreementLayout.cshtml" \
  | grep -v "Views/FundingAgreement/Partials/" \
  || true)
if [ -n "$inline_hits" ]; then
  echo "$inline_hits"
  violations=$((violations + 1))
fi

echo "[3/4] Hard-coded animation durations outside tokens.css..."
dur_hits=$(grep -RIn --include='*.css' --include='*.cshtml' \
  --exclude-dir=obj --exclude-dir=bin --exclude-dir=lib \
  -E '(transition|animation)[^;]*[[:space:]][0-9]+m?s' "$WEB_ROOT" 2>/dev/null \
  | grep -v "$TOKENS_CSS" \
  | grep -v "$CARVEOUT_DOC" \
  | grep -v "$CARVEOUT_LAYOUT" \
  || true)
if [ -n "$dur_hits" ]; then
  echo "$dur_hits"
  violations=$((violations + 1))
fi

echo "[4/4] Value-bound token names (e.g. --color-white, --motion-200ms)..."
naming_hits=$(grep -RIn --include='*.css' --include='*.cshtml' \
  --exclude-dir=obj --exclude-dir=bin --exclude-dir=lib \
  -E '--(color|motion|space|radius|shadow|type|z|font|ease)-(white|black|gray[0-9]*|red|blue|green|yellow|orange|purple|pink|[0-9]+m?s|[0-9]+px|[0-9]+rem)' \
  "$WEB_ROOT" 2>/dev/null \
  || true)
if [ -n "$naming_hits" ]; then
  echo "$naming_hits"
  violations=$((violations + 1))
fi

if [ "$violations" -gt 0 ]; then
  echo "verify-tokens: $violations gate(s) failed"
  exit 1
fi

echo "verify-tokens: all gates passed"
