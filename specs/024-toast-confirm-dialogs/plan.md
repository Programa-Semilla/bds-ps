# Implementation Plan: Consistent In-App Notifications & Confirmation Dialogs

**Branch**: `024-toast-confirm-dialogs` | **Date**: 2026-05-22 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/024-toast-confirm-dialogs/spec.md`

## Summary

Unify all result/confirmation messaging into one consistent system across every page and role: **toasts** (success/error/warning/info, top-right, success/info auto-dismiss + warning/error sticky) for operation results, and a **styled confirmation modal** for destructive actions. Replace the top-of-page banner alerts (TempData), the `window.alert` in `comparison.js`, and the 15 native `confirm()` sites. Inline field validation stays inline and gains a single summary toast.

Technical approach (from research): reuse the **already-shipped** `_ConfirmDialog`/`_ActionBar` confirm-modal infrastructure and migrate the 15 straggler `confirm()` sites onto a single shared, attribute-driven confirm modal (`data-confirm-*` + `confirm-dialog.js`, native-`confirm()` fallback). Add a new first-party toast layer (`notifications.js` + a fixed top-right `toast-container` rendered once per layout) over the vendored Bootstrap 5 `Toast` (no new dependency). Bridge the existing TempData keys to server-rendered toasts that preserve the `data-testid="success-banner"/"error-banner"` selectors. Presentation-only: **no schema change, no controller TempData-contract change**.

## Technical Context

**Language/Version**: C# 13 / .NET 10.0; client-side ES5-compatible vanilla JS (matches `site.js`)
**Primary Dependencies**: ASP.NET MVC (Razor views/layouts), vendored Tabler.io / Bootstrap 5 (`window.bootstrap.Toast` + `Modal`), jQuery (already loaded). **No new managed/CDN dependency** (NFR-001).
**Storage**: N/A — no persistence; no domain entities; no dacpac change.
**Testing**: Playwright for .NET (NUnit, Page Object Model) via `AspireFixture`; existing Unit/Integration projects untouched except where a controller helper is added.
**Target Platform**: Server-rendered web app (ASP.NET MVC), es-CR default culture.
**Project Type**: Web application (single MVC web project + test projects).
**Performance Goals**: No layout shift on toast show/dismiss (NFR-003); toast/modal added client assets a few KB each.
**Constraints**: es-CR localizable copy only (FR-010); preserve existing `data-testid` selectors where practical; native `confirm()` fallback (NFR-004); within asset budget (NFR-002 — note: `verify-asset-budget.sh` does not scan js/css).
**Scale/Scope**: 2 shared layouts, 1 FA panel partial, 1 details view, `comparison.js`, 15 confirm() sites, ~10 surfaces; new: `notifications.js`, `confirm-dialog.js`, toast CSS, 1 shared confirm-modal partial, a TempData→toast bridge partial, validation-summary-toast hook.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Notes |
|-----------|--------|-------|
| I. Clean Architecture | ✅ PASS | Change is confined to the Web layer (views, layouts, wwwroot JS/CSS, view models). No Domain/Application/Infrastructure changes; dependency rule untouched. |
| II. Rich Domain Model | ✅ N/A | No domain behavior added; presentation-only feature. |
| III. E2E Testing (NON-NEGOTIABLE) | ✅ PASS | New Playwright POM coverage for toasts + confirm modal across applicant/reviewer/admin/auth (SC-003..006); full suite green required (SC-007). |
| IV. Schema-First Database | ✅ PASS | No schema change; no EF/dacpac touch. |
| V. Specification-Driven Development | ✅ PASS | spec.md → plan.md → tasks.md → implement; 5 prioritized, independently-testable user stories. |
| VI. Simplicity / Progressive Complexity | ✅ PASS | Reuse vendored Bootstrap + existing `_ConfirmDialog`; no new dependency; notification-center/real-time/push explicitly out of scope (YAGNI). |
| Tech Standards (ASP.NET MVC, no SPA) | ✅ PASS | Thin vanilla-JS wrapper over server-rendered views; no SPA framework. |
| Quality gate: validation shown all at once | ✅ PASS | Inline validation preserved; summary toast is additive (FR-008). |

**Result:** PASS — no violations. Complexity Tracking not required.

## Project Structure

### Documentation (this feature)

```text
specs/024-toast-confirm-dialogs/
├── plan.md              # This file
├── research.md          # Phase 0 — decisions + current-state inventory
├── data-model.md        # Phase 1 — (no persistence) view-model & client contracts
├── quickstart.md        # Phase 1 — developer how-to (raise a toast / add a confirm)
├── contracts/
│   └── notifications-ui-contract.md   # Toast JS API, DOM/testid contract, data-confirm contract, TempData map
├── checklists/requirements.md
├── REVIEW-SPEC.md
├── review_brief.md
└── tasks.md             # Phase 2 — created by /speckit-tasks
```

### Source Code (repository root)

```text
src/FundingPlatform.Web/
├── Views/Shared/
│   ├── _Layout.cshtml                      # remove banner blocks; add toast-container + TempData→toast bridge include
│   ├── _AuthLayout.cshtml                  # same bridge for auth/anonymous pages
│   ├── _NotificationToasts.cshtml          # NEW — server-renders toasts from TempData (success/error/FA/validation)
│   ├── _ToastContainer.cshtml              # NEW — fixed top-right aria-live region (rendered once per layout)
│   └── Components/
│       ├── _ConfirmDialog.cshtml           # existing — CancelLabel default fix; reused unchanged otherwise
│       └── _SharedConfirmModal.cshtml      # NEW — single attribute-driven modal for migrated stragglers
├── Views/Applications/_FundingAgreementPanel.cshtml   # FA messages → toast bridge (drop in-panel alert blocks)
├── Views/Application/Details.cshtml         # ValidationErrors list → sticky error toast
├── Views/Application/Edit.cshtml            # 2 confirm() → data-confirm
├── Views/Admin/**, Views/Review/Review.cshtml         # remaining confirm() → data-confirm
├── Models/ConfirmDialogViewModel.cs         # CancelLabel default "Cancel" → "Cancelar"
└── wwwroot/
    ├── js/
    │   ├── notifications.js                 # NEW — window.Notify.toast(...) over bootstrap.Toast
    │   ├── confirm-dialog.js                # NEW — data-confirm interceptor → shared modal; native fallback
    │   └── comparison.js                    # alert() → Notify.toast(error)
    └── css/site.css                         # toast container/variant/wrap styles (minimal)

tests/FundingPlatform.Tests.E2E/
├── PageObjects/BasePage.cs                  # add toast + confirm-modal locators/helpers
├── PageObjects/**                           # update banner helpers to toast where needed
└── Tests/Notifications/                     # NEW — ToastNotificationTests, ConfirmDialogMigrationTests (POM)
```

**Structure Decision**: Single MVC web project (Option: web app). All changes live under `src/FundingPlatform.Web` (views, layouts, models, wwwroot) plus E2E tests under `tests/FundingPlatform.Tests.E2E`. No backend/Domain/Infrastructure changes.

## Phase 0 — Research

Complete. See [research.md](./research.md): current-state inventory (15 confirm() sites, TempData surfaces, AJAX alert), and five decisions (confirm-modal reuse+extend, toast layer + server-rendered bridge, validation summary toast, asset budget, testing).

## Phase 1 — Design & Contracts

- **data-model.md**: no persistence; documents the toast JS API shape, the `data-confirm-*` attribute contract, the (unchanged) TempData keys, and the `ConfirmDialogViewModel` reuse.
- **contracts/notifications-ui-contract.md**: the toast DOM/testid contract, variant→class map, aria-live behavior, the `window.Notify.toast` signature, the `data-confirm-*` interceptor contract + native fallback, and the TempData→toast mapping table.
- **quickstart.md**: how a developer raises a toast (server TempData + client JS) and guards a new destructive action.
- **Agent context**: update the `<!-- SPECKIT ... -->` plan reference in `CLAUDE.md` to point to this plan.

## Phase 2 — Tasks (created by /speckit-tasks)

Task generation will organize by user story (US1 toasts/bridge → US2 confirm migration → US3 AJAX toast → US4 validation summary → US5 a11y), each independently testable, with the 15 confirm() sites enumerated as discrete migration tasks and a coverage-matrix verification task for SC-001/SC-002.

## Complexity Tracking

No constitution violations — not applicable.
