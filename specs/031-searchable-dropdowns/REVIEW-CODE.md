# Code Review: Searchable Dropdowns

**Spec:** [spec.md](spec.md) · **Implemented:** 2026-06-11 · branch `031-searchable-dropdowns`

---

## Code Review Guide (30 minutes)

> This section guides a code reviewer through the implementation changes,
> focusing on high-level questions that need human judgment. Compliance is 100%
> (12/12 FR, 7/7 SC) — the console report has the matrix; this guide is about the
> decisions, not the checklist.

**Changed files:** 25 — 2 new source files (`searchable-select.js`, `SearchableDropdownResources.cs`), 1 CSS block, 1 layout edit, ~12 view edits (opt-in attributes), 1 JS edit (`group-drilldown-selector.js`), 6 new E2E files (helper + 5 test/seed files), plus `tasks.md` + `CLAUDE.md`.

### Understanding the changes (8 min)

- Start with [`wwwroot/js/searchable-select.js`](../../src/FundingPlatform.Web/wwwroot/js/searchable-select.js): the whole feature is one IIFE. Read the `Controller` prototype top-to-bottom — `evaluate()` is the state machine (enhance / unenhance / refresh), `enhance()` builds the combobox, `render()` filters, `onKeydown()` is the keyboard contract.
- Then [`contracts/searchable-select.md`](contracts/searchable-select.md) §2/§3 to confirm the produced DOM/ARIA + behavior match the code.
- Question: the enhancer is ~360 lines in one file with no module boundaries (matches the `wwwroot/js/*.js` house style). Is a single IIFE the right granularity, or would you want the matcher/observer split out for testability given there's no JS unit harness?

### Key decisions that need your eyes (12 min)

**Hide-in-place instead of move-into-wrapper** ([`searchable-select.js` `enhance()`](../../src/FundingPlatform.Web/wwwroot/js/searchable-select.js), relates to [FR-005](spec.md#requirements), [FR-011](spec.md#requirements))

The native `<select>` is **never moved** — the combobox wrapper is inserted as its next sibling and the select is hidden in place via a 1px clip ([`site.css` `select[data-searchable-enhanced]`](../../src/FundingPlatform.Web/wwwroot/css/site.css)). The first implementation moved the select into the wrapper and that made existing native-driven E2E (`SupplierLocationCascadeE2E`, which drives the now-enhanced cantón via `SelectOptionAsync`) intermittently flaky from mid-action DOM detach.
- Question: hiding-in-place keeps `SelectOptionAsync` working on a 1px element, which is *why* the page-object migration (T011/T020) was unnecessary — but it means the native select is technically Playwright-"visible". Comfortable with that as the long-term contract, or would you prefer enhanced controls always be driven via the combobox (and the native truly `display:none`)?

**Cascade refresh via MutationObserver, not an event** ([`searchable-select.js` `observe()` / `watchDocument()`](../../src/FundingPlatform.Web/wwwroot/js/wwwroot), relates to [FR-008](spec.md#requirements), [research R4](research.md))

A per-select `childList` observer re-runs `evaluate()` (re-snapshot + re-threshold) when cascade scripts rebuild options; a document-level observer enhances AJAX-injected selects. The enhancer never couples to `cascading-fund-filter.js` / `location-cascade.js`.
- Question: this means enhancement is *eventually* consistent (one microtask after the rebuild). E2E uses `Expect(...).ToBeVisible` polling to absorb that. Is observer-driven decoupling the right call vs. an explicit `options:rebuilt` event the cascade scripts emit?

**Group-drilldown filter is always-on, not threshold-gated** ([`group-drilldown-selector.js`](../../src/FundingPlatform.Web/wwwroot/js/group-drilldown-selector.js), relates to [FR-007](spec.md#requirements), [research R9](research.md))

The drilldown's group level is checkboxes (not a `<select>`), so it got a separate in-place text filter that appears whenever groups render — it is **not** gated by the count-7 threshold the `<select>` enhancer uses.
- Question: spec [FR-007](spec.md#requirements) names "the group-options filter" as first-class, so I left it always-on. Acceptable, or should it mirror the 7-option threshold for consistency with the rest of the feature?

### Areas where I'm less certain (5 min)

- [`searchable-select.js` `fold()`](../../src/FundingPlatform.Web/wwwroot/js/searchable-select.js): the combining-mark range U+0300–U+036F is built from char codes to keep the source ASCII. It folds `ñ`→`n` (NFD decomposes ñ). For es-CR that's arguably wrong (ñ is a distinct letter), but it makes "ñ" queries forgiving. [FR-002](spec.md#requirements) only requires accent-insensitivity; is folding ñ acceptable, or should the tilde be preserved?
- Required hidden selects: an enhanced **required** control (e.g. Process/Create Fund) keeps `required` on the 1px native select. Server validation is the real gate ([data-model validation](data-model.md#validation-rules-client-and-their-server-mirror)) and the E2E confirms the required-empty path, but browsers may log a console warning focusing a 1px field. Worth a human eyeball in a real browser.
- `aria-live` count region announces a bare number (language-neutral) rather than "N resultados" — chosen to avoid a third localized string. Is a bare count acceptable for AT, or should we add the localized phrase?

### Deviations and risks (5 min)

No deviations from [plan.md](plan.md)'s architecture. Two scoping clarifications, both recorded in [tasks.md Implementation Deviations](tasks.md#implementation-deviations):

- **T018**: [`_BranchPicker.cshtml`](../../src/FundingPlatform.Web/Views/Supplier/_BranchPicker.cshtml) renders branch **radios**, not a `<select>`, so the enhancer doesn't apply there; the supplier-branch `<select>` on [`Quotation/Edit.cshtml`](../../src/FundingPlatform.Web/Views/Quotation/Edit.cshtml) is enhanced. Question: does "supplier branches" in [FR-007](spec.md#requirements) intend the radio picker too (out of scope for a `<select>` enhancer), or is the quotation branch select sufficient?
- **E2E seed**: every cascade-fund level is ≤7 in the ephemeral seed, so US1/US2 SQL-seed 8 throwaway Funds and DELETE them in teardown. Risk: if a test fails before teardown, leftover funds could push other suites' Fund selects over threshold in the shared fixture. Question: is per-test prefix cleanup robust enough, or should this use a `[OneTimeTearDown]` safety net?

---

## Deep Review Report

> Automated multi-perspective code review results. This section summarizes
> what was checked, what was found, and what remains for human review.

**Date:** 2026-06-11 | **Rounds:** 1/3 | **Gate:** PASS

### Review Agents

| Agent | Findings | Status |
|-------|----------|--------|
| Correctness | 4 | completed |
| Architecture & Idioms | 3 | completed |
| Security | 0 | completed |
| Production Readiness | 2 | completed |
| Test Quality | 7 | completed |
| CodeRabbit (external) | - | skipped (not installed) |
| Copilot (external) | - | skipped (not installed) |

### Findings Summary

| Severity | Found | Fixed | Remaining |
|----------|-------|-------|-----------|
| Critical | 0 | 0 | 0 |
| Important | 4 | 4 | 0 |
| Minor | 10 | 6 | 4 |

### What was fixed automatically

Two enhancer correctness/robustness fixes: a `dispose()` path so per-select `MutationObserver`s and listeners no longer leak when an AJAX partial swap detaches an enhanced control (the supplier-add lookup path), and a `change`-sync listener so the combobox label stays consistent with the authoritative value if a cascade re-values a control without rebuilding options. Two accessibility/UX fixes: the empty-state `<li>` is now an `aria-live` region (announces "Sin coincidencias", not a bare count) and an unselected control now shows its placeholder instead of the "all" option's label. Four test-quality fixes closed real gaps: an enum-exclusion test (FR-009/SC-006, previously untested), a genuine FR-003 commit-then-revert test (the old required-empty test was a wrong-reason pass), Escape/Arrow keyboard coverage, a province threshold-boundary assertion, a polling fix for a non-polling negative assertion, and an `[OneTimeTearDown]` backstop against shared-fixture fund pollution.

### What still needs human attention

All Critical and Important findings were resolved and the affected E2E (SearchableDropdowns 9/9 + SupplierLocationCascade 4/4) re-ran green. Four Minor findings remain, all accepted (see [review-findings.md](review-findings.md)):

- The `fold()` matcher is duplicated in `group-drilldown-selector.js` — the reviewing agent assessed this as acceptable (frozen es-CR contract, house style). Agree, or extract a shared `text-fold.js`?
- The document-level `MutationObserver` is not scoped to dynamic regions — fine for current pages; revisit if a high-DOM-churn page is added?
- The group-drilldown checkbox filter is intentionally always-on rather than threshold-gated ([FR-007](spec.md#requirements) names it first-class). Is always-on the right call, or should it mirror the count-7 threshold?
- `scrollIntoView` is hand-rolled rather than `el.scrollIntoView({block:'nearest'})` — purely optional.

### Recommendation

All Critical/Important findings addressed and re-verified green. Code is ready for human review with no known blockers; the 4 remaining Minor findings are non-blocking and listed above for reviewer judgment.
