# Code Review: Programa Semilla Official Brand Alignment (037)

**Spec:** [spec.md](spec.md) · **Date:** 2026-06-17 · **Reviewer:** Claude (speckit.spex-gates.review-code)

## Compliance Summary

**Overall: ~100% (35/35 FR + 5/5 NFR functionally met)**

- Tokens / palette / bridge ([FR-001](spec.md#fr-001)…[FR-009](spec.md#fr-009)): compliant — `tokens.css` remap + `--tblr-primary-rgb` literal.
- Assets / logos / footer / favicon ([FR-010](spec.md#fr-010)…[FR-014](spec.md#fr-014)): compliant (see FR-010 note below).
- Chrome / components ([FR-015](spec.md#fr-015)…[FR-022](spec.md#fr-022)): compliant — dark sidebar, topbar teal, filter card + Limpiar, de-zebra tables, kebab, buttons, type.
- PDF re-tint ([FR-023](spec.md#fr-023)): compliant — two PNGs swapped, carve-out gate green, `BrandedPdf` fixture render passed (official teal).
- Sweep + verification ([FR-024](spec.md#fr-024)…[FR-031](spec.md#fr-031)): compliant — Brand E2E 39/39, axe AA incl. dark sidebar, keyboard, responsive, reduced-motion, snapshots refreshed + Users added, grep gate, asset budget 207 KB.
- Out-of-scope guardrails ([FR-032](spec.md#fr-032)…[FR-035](spec.md#fr-035)): compliant — schema diff empty, no route/permission/copy/dep changes, Tabler not upgraded.
- NFRs: NFR-002/003/004/005 compliant; NFR-001 perf is a pre-existing deferred stub gate (exits 0).

**Two minor notes (neither a functional gap):**
- **[FR-010](spec.md#fr-010)** "icon-only logo in the *collapsed* sidebar": the Tabler shell collapses to a hamburger (no icon-only rail), so the icon disc serves the favicon ([FR-013](spec.md#fr-013)) and the horizontal logo serves the expanded sidebar. Interpretation within the existing layout, not a gap.
- **[FR-017](spec.md#fr-017)**: the spec quotes the Users subtitle as "…cuentas de usuario…" (singular); the shipped code has "…cuentas de usuarios…" (plural). This string predates 037 and was left untouched per [FR-034](spec.md#fr-034).

## Filtered E2E (delivery bar, SC-016)

Brand **39/39**; admin/user (AdminUser*/UserInvitation/AdminResetPassword/LastAdminGuard/ConfirmDialogAndToast/SelfModificationGuard) **38/38**. Static gates: brand-grep PASS, asset-budget PASS (207 KB), pdf-carveout PASS, schema diff EMPTY.

---

## Code Review Guide (30 minutes)

This section guides a code reviewer through the implementation, focusing on
high-level questions that need human judgment.

**Changed files:** ~10 across 3 commits — `tokens.css` + `site.css` (the foundation),
5 shared Razor partials/layouts, the `_RowActionsMenu` component + `Models/RowActionsMenuViewModel.cs`,
`Views/Admin/Users/Index.cshtml`, 5 brand PNGs + 2 PDF PNGs (orphan SVGs deleted, 10 SVGs re-stroked),
4 gate scripts, and the E2E `Brand/` + `Admin/` test suites + `AdminUsersListPage` page object.

### Understanding the changes (8 min)

- Start with [`tokens.css`](../../src/FundingPlatform.Web/wwwroot/css/tokens.css): the ~20-token
  remap + Tabler bridge literal is the single highest-leverage change — it cascades the official
  palette platform-wide. Read the header comment (documents the spec-019→037 delta and the SC-001
  grep history note).
- Then [`site.css`](../../src/FundingPlatform.Web/wwwroot/css/site.css) (spec-037 block at the end):
  scoped dark sidebar, topbar-teal, filter card, white sidebar-logo container, and the kebab
  overflow override.
- Question: the dark-sidebar/topbar rules are scoped to `[data-testid="sidebar"]` / `[data-testid="topbar"]`
  and rely on `site.css` loading after Tabler (equal specificity → source order wins). Is keying brand
  chrome to test-id selectors acceptable, or should these be semantic class hooks?

### Key decisions that need your eyes (12 min)

**`_RowActionsMenu` as a typed-model partial vs. a TagHelper** (`Views/Shared/Components/_RowActionsMenu.cshtml`, relates to [FR-020](spec.md#fr-020))

A TagHelper would let callers move their existing forms/links in verbatim as child content,
but the project only registers framework TagHelpers (`@addTagHelper`), so a typed model
(`RowActionItem` / `RowActionsMenuViewModel`) matching the existing `_ActionBar` idiom was used.
- Question: is the typed-model shape (route/verb/confirm fields per item) the right reusable
  contract, or would a TagHelper with child content age better as more tables adopt the kebab?

**T026 — only the Users table got the kebab** (`Views/Admin/Users/Index.cshtml`)

A survey of the other 8 admin list views (Suppliers/Groups/Funds/Processes/Categories/Currencies/
ExchangeRates/ImpactTemplates) found each has a *single* row action, so per the task's
"skip single-action tables" rule none qualified.
- Question: is leaving those single actions inline the right call, or do you want the kebab applied
  uniformly for consistency even with one action?

**Confirm-attribute branching** (`Views/Shared/Components/_RowActionsMenu.cshtml`)

A conditional `data-confirm="@(hasConfirm ? "" : null)"` rendered an *empty* `data-confirm` on
non-confirm items, which `confirm-dialog.js` treated as a confirm trigger; the partial now branches
on `hasConfirm` to emit a bare `data-confirm` only for the destructive action.
- Question: confirm this preserves the exact spec-024 modal + native-`onsubmit` fallback behavior.

### Areas where I'm less certain (5 min)

- `tests/.../PageObjects/Admin/AdminUsersListPage.cs` `OpenRowActionsAsync`: the kebab E2E needed a
  pure-DOM `display`/`visibility` pin on top of the real toggle click to be deterministic in headless
  Chromium (Bootstrap dropdown auto-close race). The real root cause was a navigation race (wait for
  the row first). Is the residual style-pin acceptable test scaffolding, or should the test drive the
  dropdown purely through Bootstrap?
- [`site.css`](../../src/FundingPlatform.Web/wwwroot/css/site.css) `@media (min-width:992px)` overflow
  override on the Users table: needed so the kebab dropdown isn't clipped by `.fl-table { overflow:hidden }`
  (rounded corners). At ≥lg the table fits so overflow:visible is invisible to users; narrow keeps
  horizontal scroll. Is the lg breakpoint the right cut, and is losing the rounded-corner clip at
  desktop acceptable (the card still clips)?
- Re-stroking the 9 empty-state illustrations + `seal.svg` from spec-019 teal to `#008A9E`
  ([FR-030](spec.md#fr-030)): out of the literal "logos/footer/PDF" asset list but required for the
  grep gate to pass. Is broadening the re-stroke to all teal SVG art the right interpretation?

### Deviations and risks (5 min)

No deviations from [plan.md](plan.md) decisions D1–D13 were identified — the implementation followed
each (scoped dark sidebar D1, token remap D2/D3, PDF two-PNG swap D4, reserved orange D5, asset
mapping D6, single footer image D7, kebab D8, filter card D9, page-header token-driven D10, topbar
teal D11, gate scripts D12, E2E surface D13).

- `Views/Admin/Users/Index.cshtml` subtitle (`…usuarios…` plural) vs [FR-017](spec.md#fr-017) quote
  (`…usuario…` singular): pre-existing copy, left untouched per [FR-034](spec.md#fr-034). Question:
  is the spec's quoted copy wrong, or should the copy be corrected in a follow-up?
- NFR-001 perf gate is a pre-existing deferred stub (`compare-perf.mjs` exits 0 with a NOTICE). Risk:
  no real LCP/TBT measurement landed. Question: acceptable to inherit the deferred gate, or block on
  wiring real perf instrumentation?
