---
description: "Task list for 031-searchable-dropdowns"
---

# Tasks: Searchable Dropdowns

**Input**: Design documents from `/specs/031-searchable-dropdowns/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/searchable-select.md

**Tests**: E2E tasks are INCLUDED — Constitution Principle III makes Playwright E2E per user story non-negotiable. (No JS unit harness exists in the repo; E2E is the quality gate. No new test dependency is introduced.)

**Organization**: Tasks grouped by user story. Each story is an independently testable increment.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependency on an incomplete task)
- **[Story]**: US1 / US2 / US3 (maps to spec.md user stories)
- Paths are repo-relative; this is a single ASP.NET MVC web project (`src/FundingPlatform.Web`).

---

## Phase 1: Setup (Shared scaffolding)

**Purpose**: Localized strings, component styling, and the global script include the enhancer needs.

- [ ] T001 [P] Create `src/FundingPlatform.Web/Resources/SearchableDropdownResources.cs` — static class with es-CR consts `SearchPlaceholder = "Escriba para filtrar…"` and `NoMatchMessage = "Sin coincidencias"` (mirror the `AdminGroupsResources` const idiom).
- [ ] T002 [P] Add an `.fl-searchable*` component CSS block to `src/FundingPlatform.Web/wwwroot/css/site.css` (combobox input reuses `.form-select`; listbox, option, highlighted-option, and empty-state styles) using only semantic tokens (`--space-*`, `--color-*`, `--radius-*`, `--motion-base`) — no raw hex/px/ms per the site.css header rule.
- [ ] T003 Register the global script in `src/FundingPlatform.Web/Views/Shared/_Layout.cshtml` — add `<script src="~/js/searchable-select.js" asp-append-version="true" defer></script>` alongside `confirm-dialog.js`/`hint-tooltip.js`, and emit a single layout-level default for the es-CR empty-state/placeholder strings (e.g. `data-searchable-empty`/`data-searchable-placeholder` on `<body>`) sourced from `SearchableDropdownResources` (depends on T001).

**Checkpoint**: Build is green; strings, CSS, and the (still-empty) script reference are in place.

---

## Phase 2: Foundational (Blocking — the enhancer + E2E helper)

**Purpose**: The reusable `searchable-select.js` enhancer and the E2E combobox helper. **Every user story depends on this phase.** T004–T008 edit the same file and are sequential; T009 is a separate file.

- [ ] T004 Create `src/FundingPlatform.Web/wwwroot/js/searchable-select.js` — IIFE, ES5 house style (match `supplier-autocomplete.js`); boot enhances each `[data-searchable]` select by building the combobox DOM/ARIA structure from `contracts/searchable-select.md` §2, keeping the native `<select>` in the DOM, authoritative, `aria-hidden`/`tabindex=-1` while enhanced; derive the input `data-testid` as `<source>-search`.
- [ ] T005 In `searchable-select.js`, implement accent/case-insensitive substring filtering (`normalize('NFD')` + combining-mark strip + `toLocaleLowerCase('es')`, precomputed per option per data-model.md), live option rendering, and the "Sin coincidencias" empty-state (string from markup, not a JS literal).
- [ ] T006 In `searchable-select.js`, implement keyboard + ARIA interactions (type→filter, ↑/↓ highlight with `aria-activedescendant`, Enter/click commit, Esc close, Tab commit-or-close, `aria-live` result-count announce) and commit semantics: set `select.value` + dispatch `new Event('change',{bubbles:true})`.
- [ ] T007 In `searchable-select.js`, implement threshold gating (count selectable options with non-empty value; enhance only when `> threshold`; read `data-searchable-threshold` override, else global default `7`) and the must-pick/blur-revert rule (typed text never becomes a value; blur restores the committed value's label) — FR-003/FR-006.
- [ ] T008 In `searchable-select.js`, implement the MutationObserver layer: a document-level observer that enhances newly-injected `[data-searchable]` selects (AJAX partials), and a per-select `childList` observer that on cascade option-rebuild refreshes the filtered list, re-evaluates the threshold, re-syncs the input label to `select.value`, and clears stale query (FR-008).
- [ ] T009 [P] Create `tests/FundingPlatform.Tests.E2E/PageObjects/SearchableSelect.cs` — combobox helper per `contracts/searchable-select.md` §5 (`SelectSearchableAsync(labelFragment)`, `FilterAsync(text)`, `Options`, `EmptyState`); targets `[data-testid="<source>-search"]` and the native select for value assertions.

**Checkpoint**: A throwaway `[data-searchable]` select on any page filters, commits, and falls back to plain `<select>` with JS off. Helper compiles.

---

## Phase 3: User Story 1 — Filter a long data-driven list by typing (Priority: P1) 🎯 MVP

**Goal**: Admin filter toolbars get type-to-filter on their data-driven control (the Fund→Process→Group cascade filter), with cascade narrowing preserved.

**Independent test**: On an admin filter toolbar with an above-threshold cascade level, type a fragment, confirm only matching options remain, select one, confirm the filtered result set matches the plain-dropdown outcome; selecting a parent rebuilds the child and its search reflects the rebuilt options.

- [ ] T010 [US1] Add `data-searchable` (+ `data-searchable-placeholder`) to each cascade level `<select>` in `src/FundingPlatform.Web/Views/Shared/Components/_CascadingFundFilter.cshtml` (covers the Users/Suppliers/Processes/Reports/Applicants filter toolbars in one edit; leave `data-role`/`data-selected`/`data-testid` intact).
- [ ] T011 [US1] Migrate the cascade interactions in the affected filter-toolbar page objects (e.g. `tests/FundingPlatform.Tests.E2E/PageObjects/Admin/AdminUsersPage.cs`, `SupplierAdminPage.cs`) to the `SearchableSelect` helper for enhanced levels; keep `SelectOptionAsync` for below-threshold levels.
- [ ] T012 [US1] Add `tests/FundingPlatform.Tests.E2E/Tests/SearchableDropdowns/FilterToolbarSearchTests.cs` — type-filter an above-threshold cascade level on an admin filter toolbar, assert narrowing + that picking a Fund rebuilds Process options and the search reflects them, assert filtered results equal the plain-dropdown outcome, and assert a below-threshold level renders plain.

**Checkpoint**: US1 independently demoable — searchable admin filtering. **This is the MVP.**

---

## Phase 4: User Story 2 — Pick an entity reference while editing a form (Priority: P1)

**Goal**: Data-driven edit-form selects become searchable and commit the correct id/code; required-empty behavior unchanged. View edits T013–T019 are parallel (distinct files).

**Independent test**: On an above-threshold edit-form select, type a fragment, select, submit, and confirm the persisted entity references the same id/code the plain dropdown would have submitted.

- [ ] T013 [P] [US2] Add `data-searchable` to the Fund select in `src/FundingPlatform.Web/Views/Admin/Processes/Create.cshtml`.
- [ ] T014 [P] [US2] Add `data-searchable` to the Fund-reassignment and Plantilla-assignment selects in `src/FundingPlatform.Web/Views/Admin/Processes/Details.cshtml`.
- [ ] T015 [P] [US2] Add `data-searchable` to the eligible-Group select in `src/FundingPlatform.Web/Views/Application/Create.cshtml`.
- [ ] T016 [P] [US2] Add `data-searchable` to the Category selects in `src/FundingPlatform.Web/Views/Item/Add.cshtml`, `src/FundingPlatform.Web/Views/Item/Edit.cshtml`, and the inline add-item Category in `src/FundingPlatform.Web/Views/Application/Edit.cshtml`.
- [ ] T017 [P] [US2] Add `data-searchable` to the impact-template select in `src/FundingPlatform.Web/Views/Application/Impact.cshtml`.
- [ ] T018 [P] [US2] Add `data-searchable` to the SupplierBranch selects in `src/FundingPlatform.Web/Views/Quotation/Edit.cshtml` and `src/FundingPlatform.Web/Views/Supplier/_BranchPicker.cshtml`.
- [ ] T019 [P] [US2] Add `data-searchable` to the Currency select in `src/FundingPlatform.Web/Views/Shared/_QuoteFields.cshtml` and the source/target currency selects in `src/FundingPlatform.Web/Views/Admin/ExchangeRates/Create.cshtml` (these stay plain below threshold — verifies FR-006).
- [ ] T020 [US2] Migrate enhanced-control interactions in affected edit-form page objects (e.g. `tests/FundingPlatform.Tests.E2E/PageObjects/Admin/AdminExchangeRatesPage.cs`, the Process admin and ApplicationDraft page objects) to `SearchableSelect`.
- [ ] T021 [US2] Add `tests/FundingPlatform.Tests.E2E/Tests/SearchableDropdowns/EditFormSearchTests.cs` — search + select on an above-threshold edit-form control, submit, assert persisted value equals the plain-dropdown value (SC-002); assert a required control left empty after a no-match commit fails server validation identically to the plain dropdown (US2 scenario 2).

**Checkpoint**: US1 + US2 deliver searchable filtering and editing across the app.

---

## Phase 5: User Story 3 — Search each level of a cascading control (Priority: P2)

**Goal**: The remaining cascades get per-level search with rebuild-refresh: the Province→Cantón→Distrito location cascade and the group drilldown (Fund/Process selects + an in-place filter over the group checkbox list).

**Independent test**: Type-filter a cascade level, select a parent, confirm the child rebuilds and its search reflects the rebuilt set; in the drilldown, filter narrows the group checkboxes while accumulation/chips are preserved.

- [ ] T022 [US3] Add `data-searchable` to the Provincia/Cantón/Distrito `<select>`s in `src/FundingPlatform.Web/Views/Shared/_LocationCascade.cshtml` (AJAX-loaded child options refresh via the per-select observer from T008).
- [ ] T023 [US3] Add `data-searchable` to the Fund and Process `<select>`s in `src/FundingPlatform.Web/Views/Admin/Users/_GroupSelectorDrilldown.cshtml`.
- [ ] T024 [US3] Add an in-place text filter over the group checkbox list in `src/FundingPlatform.Web/wwwroot/js/group-drilldown-selector.js` (insert a filter input above the `[data-role="options"]` container; reuse the same NFD accent/case normalize-and-substring match; es-CR placeholder from markup; preserve checkbox accumulation/chips per spec-016/029).
- [ ] T025 [US3] Add `tests/FundingPlatform.Tests.E2E/Tests/SearchableDropdowns/CascadeSearchTests.cs` — location cascade: change Provincia, confirm Cantón search filters the newly-loaded cantones (FR-008); group drilldown: filter narrows group checkboxes and checking/unchecking still accumulates selected groups across filter changes.

**Checkpoint**: All in-scope data-driven controls are searchable; cascade rebuild-refresh verified end-to-end.

---

## Phase 6: Polish & Cross-Cutting

**Purpose**: Cross-cutting guarantees and the delivery gate.

- [ ] T026 [P] Add `tests/FundingPlatform.Tests.E2E/Tests/SearchableDropdowns/ProgressiveEnhancementTests.cs` — with `JavaScriptEnabled=false` browser context, confirm one of each control type still selects + submits via the native `<select>` (FR-011/SC-005).
- [ ] T027 [P] Run `bash scripts/verify-asset-budget.sh` and confirm green (SC-007; JS/CSS not counted, expected pass).
- [ ] T028 [P] Accessibility smoke: verify combobox role/`aria-expanded`/`aria-activedescendant`/`aria-live` and keyboard-only operability on one enhanced control; capture findings in the PR description (FR-004/SC-004).
- [ ] T029 Run the filtered E2E delivery gate: `dotnet test tests/FundingPlatform.Tests.E2E --filter "FullyQualifiedName~SearchableDropdowns"` plus the migrated suites touched in T011/T020 (e.g. `~AdminUser`, `~Supplier`, `~ExchangeRates`, `~Process`); confirm green before claiming delivery.
- [ ] T030 [P] Update `CLAUDE.md` Recent Changes + the SPECKIT marker with the shipped 031 summary and E2E result (after T029 is green).

---

## Dependencies & Execution Order

- **Phase 1 (Setup)** → **Phase 2 (Foundational)** → **Phases 3–5 (User Stories)** → **Phase 6 (Polish)**.
- **Foundational blocks everything**: T004–T009 must complete before any US task. T004→T005→T006→T007→T008 are sequential (same file); T009 is parallel to them.
- **User stories are independent of each other** (distinct view files) and may proceed in any order or in parallel once Foundational is done. Recommended priority order: US1 → US2 → US3.
- T003 depends on T001. T010/T013–T019/T022–T023 depend on Phase 2. E2E test tasks depend on their story's view edits + the relevant page-object migration.
- T029 (gate) depends on all story phases; T030 depends on T029.

## Parallel Execution Examples

- **Setup**: T001 and T002 in parallel; then T003.
- **Foundational**: T009 (helper) in parallel with the T004→T008 chain.
- **US2 view edits**: T013, T014, T015, T016, T017, T018, T019 all in parallel (distinct files), then T020 (page objects) and T021 (E2E).
- **Polish**: T026, T027, T028 in parallel; T030 in parallel after T029.

## Implementation Strategy

- **MVP = Phase 1 + Phase 2 + Phase 3 (US1)** — searchable admin filter toolbars. Independently shippable and demoable.
- **Incremental delivery**: add US2 (edit-form search) then US3 (remaining cascades), each green-gated by its E2E before moving on.
- **Delivery gate** (per CLAUDE.md): the filtered E2E suites for the affected surfaces must be personally executed and green (T029). Full ~30-min E2E suite only if a reviewer deems this cross-cutting or on request.

## Task Count

- Total: **30 tasks** (T001–T030).
- Setup: 3 · Foundational: 6 · US1: 3 · US2: 9 · US3: 4 · Polish: 5.
- Parallel opportunities: 7 `[P]` view edits in US2, plus Setup/Foundational/Polish parallel pairs.
