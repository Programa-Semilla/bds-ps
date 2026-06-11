# Implementation Plan: Searchable Dropdowns

**Branch**: `031-searchable-dropdowns` | **Date**: 2026-06-11 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/031-searchable-dropdowns/spec.md`

## Summary

Add type-to-filter autocomplete to data-driven dropdowns across filter toolbars and entity-edit forms, leaving static enum dropdowns untouched. Technical approach: one in-house vanilla-JS progressive enhancer (`searchable-select.js`) that wraps any `<select data-searchable>` into an accessible combobox (text input + filtered listbox), keeping the native `<select>` in the DOM as the authoritative, posted value. Matching is case- and accent-insensitive (es-CR). Enhancement is gated on a configurable option-count threshold (default 7) and re-evaluated when a control's options are rebuilt at runtime (cascades). No server, DTO, route, or schema changes; no new vendored or managed dependency.

## Technical Context

**Language/Version**: C# / .NET 10 (Razor views, resource classes); browser ES5-compatible vanilla JavaScript (matches existing `wwwroot/js/*.js` house style — IIFE, `var`, no build step)
**Primary Dependencies**: None new. Reuses vendored Tabler.io CSS (`.form-select`), the `tokens.css` semantic CSS variables, and existing resource-class localization. No NuGet, no front-end package.
**Storage**: N/A (presentation-only; no schema, no EF change)
**Testing**: Playwright E2E (NUnit, Page Object Model) via `AspireFixture`; existing Unit/Integration suites unaffected (no server code changes beyond two es-CR resource strings)
**Target Platform**: Server-rendered ASP.NET MVC app, modern evergreen browsers
**Project Type**: Web application (ASP.NET MVC, server-side rendering) — front-end progressive enhancement
**Performance Goals**: Filtering is client-side over already-rendered options; perceptually instant (<16ms per keystroke for lists in the low hundreds). No network calls introduced.
**Constraints**: No CDN; no new vendored/managed dependency (FR-012). Progressive enhancement — plain `<select>` must work with JS disabled (FR-011). es-CR copy (FR-010). Asset-budget check must still pass (JS/CSS are not counted by `scripts/verify-asset-budget.sh`, which measures only fonts/illustrations/brand assets against a 400 KB gz limit — so no budget impact).
**Scale/Scope**: ~10 data-driven control sites (several in shared partials), one new JS module (~6–10 KB raw), one small CSS block, two es-CR resource strings, one E2E page-object helper, and per-user-story E2E coverage.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Assessment | Status |
|---|---|---|
| I. Clean Architecture | Change is confined to the Web layer (views, `wwwroot/`, one Resources class). No Domain/Application/Infrastructure changes; dependency rule untouched. | PASS |
| II. Rich Domain Model | No domain logic involved (presentation-only). | PASS (N/A) |
| III. End-to-End Testing (NON-NEGOTIABLE) | Each user story gets Playwright E2E coverage (filter-toolbar search, edit-form search + persisted value, cascade per-level search). Page Object Model extended with a combobox helper. | PASS |
| IV. Schema-First Database | No schema change; dacpac untouched (FR-005, SC-003). | PASS |
| V. Specification-Driven Development | Produced via brainstorm → spec → (this) plan → tasks. | PASS |
| VI. Simplicity & Progressive Complexity | Simplest viable approach: one small in-house enhancer over existing markup; no library; remote/server-side search explicitly deferred. Threshold default sensible + configurable. | PASS |

**Result**: No violations. Complexity Tracking not required.

**Post-Design re-check (after Phase 1)**: Still PASS — design adds only a JS module, a CSS block, two resource strings, opt-in attributes on existing selects, and an E2E helper. No new dependency, no layer crossing, no schema change.

## Project Structure

### Documentation (this feature)

```text
specs/031-searchable-dropdowns/
├── plan.md              # This file
├── research.md          # Phase 0 output — decisions & rationale
├── data-model.md        # Phase 1 output — (no entities; component state model)
├── quickstart.md        # Phase 1 output — how to enhance a control + test it
├── contracts/
│   └── searchable-select.md   # Markup/ARIA/behavior contract + E2E helper signature
├── spec.md              # Feature spec
├── REVIEW-SPEC.md       # Spec-review gate result
├── review_brief.md      # Reviewer guide
└── tasks.md             # Phase 2 output (/speckit-tasks — NOT created here)
```

### Source Code (repository root)

```text
src/FundingPlatform.Web/
├── wwwroot/
│   ├── js/
│   │   └── searchable-select.js          # NEW — the enhancer (IIFE, MutationObserver-driven)
│   └── css/
│       └── site.css                      # EDIT — small .fl-searchable-* component block
├── Resources/
│   └── SearchableDropdownResources.cs    # NEW — es-CR placeholder + empty-state strings
└── Views/
    ├── Shared/
    │   ├── _Layout.cshtml                # EDIT — load searchable-select.js globally (defer)
    │   ├── _LocationCascade.cshtml        # EDIT — data-searchable on the 3 cascade selects
    │   └── Components/
    │       └── _CascadingFundFilter.cshtml # EDIT — data-searchable on each cascade level
    ├── Admin/
    │   ├── Processes/{Create,Details}.cshtml   # EDIT — Fund + Plantilla selects
    │   ├── ExchangeRates/Create.cshtml         # EDIT — source/target currency selects
    │   └── Users/_GroupSelectorDrilldown.cshtml# EDIT — fund/process selects + group-checkbox filter
    ├── Application/{Create,Edit,Impact}.cshtml # EDIT — Group / Category / Template selects
    ├── Item/{Add,Edit}.cshtml                  # EDIT — Category select
    ├── Quotation/Edit.cshtml                   # EDIT — SupplierBranch select
    ├── Supplier/_BranchPicker.cshtml           # EDIT — SupplierBranch select
    └── Shared/_QuoteFields.cshtml              # EDIT — Currency select (threshold no-ops small lists)

src/FundingPlatform.Web/wwwroot/js/group-drilldown-selector.js  # EDIT — inline filter over group checkboxes

tests/FundingPlatform.Tests.E2E/
├── PageObjects/
│   └── SearchableSelect.cs               # NEW — combobox helper (SelectSearchableAsync, FilterAsync)
└── Tests/
    └── SearchableDropdowns/              # NEW — per-user-story E2E (filter toolbar, edit form, cascade)
```

**Structure Decision**: Single ASP.NET MVC web project (existing). All work lives in `FundingPlatform.Web` (front-end + two resource strings) plus E2E tests. The enhancer is loaded globally in `_Layout.cshtml` (like `confirm-dialog.js`/`hint-tooltip.js`) so every page with a `[data-searchable]` select is covered without per-view `@section Scripts`. Opt-in is per-`<select>` via `data-searchable`, placed in shared partials/components where possible to cover many call sites with few edits.

## Key Design Decisions (detail in research.md / contracts/)

1. **Opt-in via `data-searchable` attribute** (not auto-detection): explicit, safe against catching static enums, matches the data-* config house style. Optional `data-searchable-threshold="N"` per control overrides the global default (7).
2. **Native `<select>` stays authoritative**: the enhancer hides the native select from pointer use, renders a combobox (text input + `role=listbox`), and on commit sets `select.value` + dispatches a bubbling `change` — so form submission and the existing cascade JS keep working unchanged (FR-005).
3. **MutationObserver-driven**: a document-level observer enhances newly-added `[data-searchable]` selects (AJAX partials); a per-select observer on `childList` refreshes the combobox when cascade logic rebuilds options and re-evaluates the threshold (FR-008).
4. **Accent/case-insensitive matching** via `String.prototype.normalize('NFD')` + diacritic strip + `toLocaleLowerCase` (FR-002).
5. **Must-pick-from-list** (FR-003): typed text only filters; blur with no committed selection reverts the input display to the select's current value label; it never writes a new value.
6. **Group-drilldown checkbox filter**: the drilldown's group level is checkboxes, not a `<select>`, so it gets a small text-filter input handled inside `group-drilldown-selector.js` (not the generic enhancer). Its fund/process selects use the generic enhancer.
7. **E2E**: page objects for enhanced controls use a new `SearchableSelect` helper that types into the combobox and clicks the matching option. The native select remains present so `SelectOptionAsync` stays a viable fallback for unaffected/below-threshold controls.

## Complexity Tracking

> No Constitution Check violations — section intentionally empty.
