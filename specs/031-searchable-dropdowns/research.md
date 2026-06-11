# Phase 0 Research: Searchable Dropdowns

All "NEEDS CLARIFICATION" items from the spec's open threads are resolved below. Decisions are grounded in the existing codebase patterns (see file references).

## R1 — Delivery mechanism: in-house enhancer vs. library

- **Decision**: One in-house vanilla-JS module `wwwroot/js/searchable-select.js`, IIFE, ES5-compatible, no build step — mirroring `supplier-autocomplete.js` and `location-cascade.js`.
- **Rationale**: Repo conventions forbid CDNs and require spec approval for new managed/vendored deps; "reuse what is vendored" is the default posture (CLAUDE.md Conventions). A combobox over already-rendered options is small enough to own. No asset-budget impact — `scripts/verify-asset-budget.sh` only counts fonts/illustrations/brand SVGs against a 400 KB gz cap; `wwwroot/js` and `wwwroot/css` are excluded.
- **Alternatives considered**: Tom Select / select2 (new vendored asset needing approval + budget scrutiny; heavier than need); native `<datalist>` (poor keyboard UX, no reliable id binding, inconsistent cross-browser).

## R2 — Opt-in mechanism: `data-searchable` attribute vs. auto-detection

- **Decision**: Explicit opt-in via a `data-searchable` boolean attribute on each in-scope `<select>`. Optional per-control `data-searchable-threshold="N"`. A global default threshold constant (`7`) lives in the JS module.
- **Rationale**: Auto-detecting "data-driven" selects is impossible to do safely from the client — a `<select>` carrying enum values (status/role) is indistinguishable in the DOM from one carrying entity ids. Explicit opt-in guarantees FR-009 (static enums excluded) and matches the data-* configuration idiom used by `location-cascade.js`/`cascading-fund-filter.js`. Most in-scope controls live in shared partials (`_CascadingFundFilter.cshtml`, `_LocationCascade.cshtml`, `_QuoteFields.cshtml`, `_BranchPicker.cshtml`), so a handful of edits cover many call sites.
- **Alternatives considered**: Opt-out (`data-no-search` on enums) — fragile, easy to forget on a new enum select, violates fail-safe. Convention-based class detection — same indistinguishability problem.

## R3 — Keeping the native `<select>` authoritative (form binding + cascades)

- **Decision**: The native `<select>` remains in the DOM and is the single source of the posted value. The enhancer visually replaces it with a combobox; on selection it sets `select.value` and dispatches `new Event('change', { bubbles: true })`. The select is hidden from pointer/AT focus while enhanced (the combobox carries the accessible name).
- **Rationale**: FR-005/SC-002/SC-003 require zero server-contract change and value equivalence. The existing cascade scripts listen for `change` on the parent select and rebuild child options — dispatching a bubbling `change` preserves that chain exactly. `asp-for`/`asp-items` model binding is by `name`/`value`, which is untouched.
- **Alternatives considered**: Replacing the select with a hidden `<input>` mirror — would break `asp-validation-for`, the cascade `data-role` selectors, and `selectOption`-style automation. Rejected.

## R4 — Tracking runtime option rebuilds (cascades, AJAX partials)

- **Decision**: Two `MutationObserver`s. (a) A document-level observer watches for added nodes and enhances any new `[data-searchable]` select (covers AJAX-injected partials like the supplier branch picker / new-supplier inline form). (b) A per-select observer on `childList` fires when cascade JS rebuilds `<option>`s, prompting the enhancer to rebuild its filtered list, re-sync the displayed label to `select.value`, and re-evaluate the threshold (show combobox only when current selectable-option count > threshold).
- **Rationale**: FR-008 requires the search view to refresh with no stale option. `MutationObserver` decouples the enhancer from cascade internals (no event contract to add to `cascading-fund-filter.js`). It also matches the AJAX-resilience goal that `location-cascade.js` achieves via event delegation.
- **Alternatives considered**: A custom `options:rebuilt` event emitted by each cascade script — couples the enhancer to every producer and risks missed emissions. Polling — wasteful. Both rejected.

## R5 — Matching semantics (es-CR, accent/case-insensitive)

- **Decision**: Normalize both the query and each option's display text with `text.normalize('NFD').replace(/[̀-ͯ]/g, '').toLocaleLowerCase('es')`, then substring-match. Match against the option's visible text content.
- **Rationale**: FR-002 — "jose" must match "José", "CARTAGO" must match "Cartago". NFD + combining-mark strip is the standard, dependency-free diacritic fold. Evergreen-browser `normalize` support is universal.
- **Alternatives considered**: A hand-rolled accent map (incomplete, error-prone); `Intl.Collator` with sensitivity:'base' (great for compare/sort, awkward for substring search). Rejected.

## R6 — Threshold semantics

- **Decision**: Count **selectable** options (those with a non-empty `value`, excluding the leading placeholder/"all" option). Enhance only when that count `>` threshold (default 7). On a below-threshold control, the native select renders normally (no combobox), even if `data-searchable` is present. The threshold is re-evaluated on option rebuild (R4).
- **Rationale**: FR-006 — a search box over 2–3 currencies is noise. Excluding the placeholder makes "7" mean seven real choices. Re-evaluation lets a cascade child that starts small but grows after a parent selection become searchable when it actually has many options.
- **Alternatives considered**: Counting all options incl. placeholder (off-by-one confusion). Fixed global-only threshold with no per-control override (less flexible for the cascade group level). Rejected the rigid variant; kept per-control override.

## R7 — Must-pick-from-list & blur behavior

- **Decision**: Typing filters only. Commit happens by clicking an option or pressing Enter on the highlighted option, which sets `select.value`. On blur with no fresh commit, the combobox input text is reset to the label of the select's **current** value (or the placeholder for an empty/"all" filter). Typed text never becomes a value.
- **Rationale**: FR-003 — these bind to entity ids/foreign keys; fabricating a value from free text would corrupt the post. Reverting the display on blur avoids a misleading "typed but not selected" state.

## R8 — Accessibility (ARIA combobox)

- **Decision**: Follow the WAI-ARIA combobox-with-listbox pattern: input `role="combobox"`, `aria-expanded`, `aria-controls` → the listbox `id`; listbox `role="listbox"` with `role="option"` children; active option tracked via `aria-activedescendant`; the input is associated with the original `<label>` (reuse its `for`/`id`). Keyboard: type to filter, ↑/↓ move highlight, Enter commit, Esc close without change, Tab commits highlight or closes. An `aria-live="polite"` region announces the filtered result count / "Sin coincidencias".
- **Rationale**: FR-004/SC-004 — keyboard-only operability and AT announceability. Reusing the existing `<label>` preserves the accessible name already authored in the views.
- **Alternatives considered**: `role="searchbox"` (loses listbox semantics); no live region (fails the "announce filtered count" criterion). Rejected.

## R9 — Group-drilldown group level (checkboxes, not a `<select>`)

- **Decision**: The spec-029 group drilldown renders the group level as a checkbox list (`group-drilldown-selector.js`), so the generic enhancer (which targets `<select>`) does not apply there. Add a small text-filter input above the checkbox container, handled inside `group-drilldown-selector.js`, using the same normalize-and-substring matcher (shared via a tiny exported helper or duplicated 3-line function). The drilldown's Fund and Process `<select>`s get the generic `data-searchable` enhancer.
- **Rationale**: FR-007 names "the group-options filter of the group drilldown." The checkbox semantics (multi-select with accumulating chips) must be preserved (spec-016/029), so a filter-in-place is correct, not a combobox.
- **Alternatives considered**: Converting the checkbox list to a multi-select combobox — would change the established drilldown UX and risk regressions in spec-029 E2E. Rejected.

## R10 — E2E interaction strategy

- **Decision**: Add a `SearchableSelect` page-object helper (`tests/.../PageObjects/SearchableSelect.cs`) exposing e.g. `SelectSearchableAsync(testId, label)` (type the label fragment, then click the matching `role=option`) and `FilterAsync(testId, text)`. Migrate interactions with enhanced controls in affected page objects to this helper. Below-threshold / non-enhanced controls keep using `SelectOptionAsync`. The native select remains present, so `SelectOptionAsync` is a documented fallback.
- **Rationale**: Conventions allow E2E rewrites when UI is elevated ("UX/UI quality wins over E2E selector stability"). Driving the combobox is the faithful user-path test and avoids betting on Playwright's actionability against a visually-hidden select. `data-testid` stays on the original `<select>`; the enhancer mirrors it (or derives a child testid) so existing selectors resolve.
- **Alternatives considered**: Forcing `SelectOptionAsync` on a hidden select with `force:true` everywhere (tests the model, not the UI; brittle across the visibility technique). Rejected as the primary path; kept as fallback.

## R11 — Localization of new copy

- **Decision**: New `Resources/SearchableDropdownResources.cs` static class with `const string SearchPlaceholder = "Escriba para filtrar…";` and `const string NoMatchMessage = "Sin coincidencias";`. The enhancer reads these from `data-*` attributes the views emit (so JS carries no Spanish literals): the global script picks up per-control `data-searchable-placeholder` / a body-level default, or the values are written onto each enhanced select via the shared partials. A single hidden `<template>`/`data-*` default in `_Layout.cshtml` supplies the strings once.
- **Rationale**: Matches the C# `const` resource-class idiom (`AdminGroupsResources` etc.); keeps copy out of JS; satisfies FR-010 and the spec-016 NFR that every admin string localizes.
- **Alternatives considered**: Hard-coding the Spanish in JS (violates the no-copy-in-partials/keep-strings-localizable convention). Rejected.

## Summary of resolved unknowns

| Open thread (from spec/brainstorm) | Resolution |
|---|---|
| Opt-in mechanism (`data-searchable` vs auto-detect) | **`data-searchable` attribute** (R2) |
| Playwright targets native select vs combobox input | **Combobox helper `SearchableSelect`; native select kept as fallback** (R10) |
| How runtime option rebuilds refresh the view | **Per-select `MutationObserver` on childList** (R4) |
| Threshold counting | **Selectable (non-placeholder) options, `>` 7, per-control override** (R6) |
| es-CR copy delivery | **`SearchableDropdownResources` const class, surfaced via data-* (no JS literals)** (R11) |
| Group drilldown group level | **In-place checkbox text filter inside `group-drilldown-selector.js`** (R9) |
