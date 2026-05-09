#!/usr/bin/env bash
# Spec 019 T030 — brand-pivot grep gate.
#
# Fails build on:
#   (a) Legacy spec-011 palette hex values outside tokens.css history comments
#       and wwwroot/lib/brand/pdf/ (FR-038/FR-039 — PDF assets are pinned by spec 018).
#   (b) Literal "Forge" or "Capital Semilla" outside git history,
#       archived specs/011-warm-modern-facelift/BRAND-VOICE.md, and the
#       specs/ / brainstorm/ / CHANGELOG.md documents (SC-002).
#   (c) Yellow accent (#F2C014 or --color-accent) used in semantic-context
#       selectors per research R11 keyword heuristics (NFR-003).
#
# Each violation prints file:line that triggered it.

set -u

REPO_ROOT="$(git -C "$(dirname "$0")" rev-parse --show-toplevel 2>/dev/null || pwd)"
cd "$REPO_ROOT" || exit 2

WEB_ROOT="src/FundingPlatform.Web"
TOKENS_CSS="$WEB_ROOT/wwwroot/css/tokens.css"

violations=0

echo "[1/3] Legacy palette hex outside tokens.css and wwwroot/lib/brand/pdf/..."
LEGACY_HEX_PATTERN='#2E5E4E|#1F4438|#E1ECE6|#D98A1B|#FBEED6|#FAF7F2|#F4EFE6|#E5DED2'
legacy_hits=$(grep -RIn --include='*.css' --include='*.cshtml' --include='*.js' --include='*.svg' \
  --exclude-dir=obj --exclude-dir=bin --exclude-dir=node_modules \
  -E "$LEGACY_HEX_PATTERN" "$WEB_ROOT" 2>/dev/null \
  | grep -v "$TOKENS_CSS" \
  | grep -v "$WEB_ROOT/wwwroot/lib/brand/pdf/" \
  || true)
if [ -n "$legacy_hits" ]; then
  echo "$legacy_hits"
  violations=$((violations + 1))
fi

echo "[2/3] Literal 'Forge' / 'Capital Semilla' outside allowed paths..."
# Spec 019 SC-002: drift-name absence required in running views, layouts,
# partials, brand SVGs, email templates, and BRAND-VOICE.md. Test-side files
# under tests/ may legitimately reference the strings in regression tests
# (e.g. EmailTemplateSenderTests.cs documenting what to assert *absence* of);
# we exclude tests/ from the running-views drift gate.
NAME_HITS=$(grep -RIn -E "Capital Semilla|\bForge\b" \
  --include='*.cs' --include='*.cshtml' --include='*.json' --include='*.js' \
  --include='*.css' --include='*.svg' --include='*.html' \
  --exclude-dir=obj --exclude-dir=bin --exclude-dir=.git --exclude-dir=node_modules \
  --exclude-dir=specs --exclude-dir=brainstorm \
  src/ 2>/dev/null \
  || true)
# Strip historical comment / banner allowances inside tokens.css.
NAME_HITS=$(echo "$NAME_HITS" | grep -v "^$TOKENS_CSS:" || true)
if [ -n "$NAME_HITS" ]; then
  echo "$NAME_HITS"
  violations=$((violations + 1))
fi

echo "[3/3] Yellow accent in semantic-context selectors (NFR-003)..."
# Heuristic per research R11: yellow (--color-accent / #F2C014) MUST NOT carry
# semantic meaning. Flag rules whose selector carries semantic-state intent
# AND whose body sets the high-saturation accent value as a state-bearing prop.
# The decorative subtle variant (--color-accent-subtle) is allowed as a fill
# (low contrast hazard is mitigated by dark-text overlay per FR-021).
#
# Trigger keywords (whole-word, hyphen-bounded): -error, -danger, -warning,
# -invalid, focus-ring, icon-status-, icon-warning-, icon-error-.
# Note: ".fl-status-pill" is a generic UI primitive and NOT semantic by itself —
# its meaning is keyed by data-tone="…" — so plain "status" is excluded.
ACCENT_HITS=$(grep -RInE \
  '(\.[a-z]*-(error|danger|warning|invalid)|-?error[^a-z-]|-?danger[^a-z-]|-?warning[^a-z-]|-?invalid[^a-z-]|focus-ring|icon-status-|icon-warning-|icon-error-)[^\{]*\{[^}]*(var\(--color-accent\)|#F2C014\b)' \
  --include='*.css' --include='*.cshtml' \
  --exclude-dir=obj --exclude-dir=bin \
  "$WEB_ROOT" 2>/dev/null \
  || true)
# Also flag outline-color: accent on any rule (focus semantics).
ACCENT_OUTLINE_HITS=$(grep -RInE 'outline-color\s*:\s*(var\(--color-accent\)|#F2C014\b)' \
  --include='*.css' --include='*.cshtml' \
  --exclude-dir=obj --exclude-dir=bin \
  "$WEB_ROOT" 2>/dev/null \
  || true)
ACCENT_ALL=$(printf "%s\n%s\n" "$ACCENT_HITS" "$ACCENT_OUTLINE_HITS" | grep -v '^$' || true)
if [ -n "$ACCENT_ALL" ]; then
  echo "$ACCENT_ALL"
  violations=$((violations + 1))
fi

if [ "$violations" -gt 0 ]; then
  echo "brand-grep-gate: $violations gate(s) failed"
  exit 1
fi

echo "brand-grep-gate: all gates passed"
