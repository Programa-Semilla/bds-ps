# Tasks: Consistent In-App Notifications & Confirmation Dialogs

**Input**: Design documents from `/specs/024-toast-confirm-dialogs/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/notifications-ui-contract.md

**Tests**: E2E tests are REQUIRED for this feature (Constitution III — NON-NEGOTIABLE; spec SC-007). Each user story includes Playwright POM coverage.

**Organization**: Tasks grouped by user story. US1 + US2 are the P1 MVP.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependency on incomplete tasks)
- All paths are relative to repo root (`src/FundingPlatform.Web/…`, `tests/FundingPlatform.Tests.E2E/…`)

## Path Conventions

Web app — single MVC web project. All production code under `src/FundingPlatform.Web`; E2E under `tests/FundingPlatform.Tests.E2E`. No Domain/Application/Infrastructure/dacpac changes.

---

## Phase 1: Setup

- [X] T001 [P] Confirm asset-budget scripts (`scripts/verify-asset-budget.sh`, `scripts/asset-budget-check.sh`) do not scan `wwwroot/js`/`wwwroot/css` (per research Decision 4); record baseline so NFR-002 stays green after new JS/CSS.

---

## Phase 2: Foundational (Blocking Prerequisites)

**⚠️ CRITICAL**: The shared toast + confirm client infrastructure below must exist before any user story is implemented.

- [X] T002 [P] Create `src/FundingPlatform.Web/wwwroot/js/notifications.js` exposing `window.Notify.toast({variant,message,sticky})` + `Notify.success/error/warning/info` over `window.bootstrap.Toast`; success/info → `autohide:true, delay:5000`, warning/error → `autohide:false` (sticky); no-op safe if `bootstrap.Toast` absent. (FR-001, FR-004, FR-005)
- [X] T003 [P] Create `src/FundingPlatform.Web/Views/Shared/_ToastContainer.cshtml` — fixed top-right container (`position-fixed top-0 end-0 p-3`, `data-testid="toast-container"`, z-index above content) with aria-live semantics (polite for success/info, assertive for warning/error). (FR-005, FR-013, NFR-003)
- [X] T004 [P] Add toast styles to `src/FundingPlatform.Web/wwwroot/css/site.css` — max-width, body wrap, stacking gap, z-index; no layout shift. (NFR-003)
- [X] T005 [P] Fix es-CR default: change `CancelLabel = "Cancel"` → `"Cancelar"` in `src/FundingPlatform.Web/Models/ConfirmDialogViewModel.cs`. (FR-007, FR-010)
- [X] T006 Create `src/FundingPlatform.Web/Views/Shared/Components/_SharedConfirmModal.cshtml` (single shared modal mirroring `_ConfirmDialog` styling + `data-testid="confirm-dialog"/"confirm-button"/"cancel-button"`) and `src/FundingPlatform.Web/wwwroot/js/confirm-dialog.js` — intercept `[data-confirm]` elements, populate+open the shared modal from `data-confirm-*` attributes, submit the originating form (or follow link) on confirm, return focus to trigger on close; keep native `confirm()` fallback active until JS initializes. (FR-006, FR-007, FR-012, NFR-004)
- [X] T007 Reference `notifications.js` + `confirm-dialog.js` (after `tabler.min.js`) and include `_ToastContainer` + `_SharedConfirmModal` once each in `src/FundingPlatform.Web/Views/Shared/_Layout.cshtml` and `_AuthLayout.cshtml`.

**Checkpoint**: Toast API + shared confirm modal load on every page; nothing user-visible changes yet.

---

## Phase 3: User Story 1 — Server-side result toasts (Priority: P1) 🎯 MVP

**Goal**: Every TempData server outcome surfaces as a consistent top-right toast (success auto-dismiss, error sticky) on all roles' pages; the top-of-page banner blocks are gone.

**Independent Test**: Trigger a success and an error TempData message as applicant, admin, and on an auth page → consistent toasts appear; no banner alert renders; refresh does not re-show (FR-011).

- [X] T008 [US1] Create `src/FundingPlatform.Web/Views/Shared/_NotificationToasts.cshtml` — server-render a toast for each present `TempData["SuccessMessage"]` (variant success, `data-testid="success-banner"`) and `["ErrorMessage"]` (variant error, sticky, `data-testid="error-banner"`) into the toast container; shown on load by `notifications.js`. (FR-002, FR-011)
- [X] T009 [US1] In `src/FundingPlatform.Web/Views/Shared/_Layout.cshtml`, remove the `.alert.alert-success/-danger` banner blocks (lines ~217–230) and include `_NotificationToasts`. (FR-002, SC-002)
- [X] T010 [US1] In `src/FundingPlatform.Web/Views/Shared/_AuthLayout.cshtml`, remove the `.fl-alert` banner blocks and include `_NotificationToasts`. (FR-002, SC-002)
- [X] T011 [US1] Route `TempData["FundingAgreementSuccess"]/["FundingAgreementError"]` through `_NotificationToasts` (success/error toasts with `data-testid="funding-agreement-success"/"-error"`) and remove the in-panel `.alert` blocks in `src/FundingPlatform.Web/Views/Applications/_FundingAgreementPanel.cshtml` (lines 15–25). (FR-002)
- [X] T012 [US1] Convert the `TempData["ValidationErrors"]` submit-blocking list in `src/FundingPlatform.Web/Views/Application/Details.cshtml` (line ~73) into a sticky error toast (`data-testid="validation-summary-toast"`) via the bridge; remove the in-place list alert. (FR-002)
- [X] T013 [P] [US1] E2E: `tests/FundingPlatform.Tests.E2E/Tests/Notifications/ToastNotificationTests.cs` + toast locators/helpers in `tests/FundingPlatform.Tests.E2E/PageObjects/BasePage.cs` — assert success toast appears & auto-dismisses, error toast appears & stays, across an applicant page, an admin page, and an auth page; assert no top-of-page banner; uses `data-testid="success-banner"/"error-banner"`. (SC-002, SC-003)

**Checkpoint**: US1 independently testable and demoable.

---

## Phase 4: User Story 2 — Styled confirm modal for destructive actions (Priority: P1)

**Goal**: All 15 native `confirm()` sites use the shared styled modal; confirm proceeds, cancel/Esc aborts with no side effect.

**Independent Test**: For a representative subset, trigger the action → styled modal with correct es-CR copy; confirm runs the action; cancel/Esc aborts with no state change.

- [X] T014 [P] [US2] Migrate `src/FundingPlatform.Web/Views/Admin/Groups/Edit.cshtml:57` (delete group) to `data-confirm-*`, preserving the typed-name guard intent in the body copy.
- [X] T015 [P] [US2] Migrate `src/FundingPlatform.Web/Views/Admin/Plantillas/Index.cshtml:93` (archive plantilla) to `data-confirm-*`.
- [X] T016 [US2] Migrate the 3 sites in `src/FundingPlatform.Web/Views/Admin/Processes/Details.cshtml` (lines 101 unassign, 115 force unassign, 301 close process) to `data-confirm-*`.
- [X] T017 [US2] Migrate the 2 sites in `src/FundingPlatform.Web/Views/Admin/PublicLanding/Index.cshtml` (lines 80, 138 — delete Reglamento / cotización example) to `data-confirm-*`.
- [X] T018 [P] [US2] Migrate `src/FundingPlatform.Web/Views/Admin/Suppliers/Detail.cshtml:98` (verify supplier) to `data-confirm-*`, passing `AdminSuppliersResources.Verify_Confirm` as `data-confirm-body`. (FR-007)
- [X] T019 [P] [US2] Migrate `src/FundingPlatform.Web/Views/Admin/Users/ResetPassword.cshtml:40` (reset password) to `data-confirm-*`.
- [X] T020 [P] [US2] Migrate `src/FundingPlatform.Web/Views/Admin/Users/Index.cshtml:181` (disable user) to `data-confirm-*`.
- [X] T021 [US2] Migrate the 2 sites in `src/FundingPlatform.Web/Views/Applications/_FundingAgreementPanel.cshtml` (lines 63 overwrite, 121 withdraw upload) to `data-confirm-*`.
- [X] T022 [US2] Migrate the 2 row-level sites in `src/FundingPlatform.Web/Views/Application/Edit.cshtml` (lines 232 delete item, 330 delete quotation) to `data-confirm-*`, ensuring the originating per-row form is the one submitted on confirm.
- [X] T023 [P] [US2] Migrate `src/FundingPlatform.Web/Views/Review/Review.cshtml:393` (return application) to `data-confirm-*`.
- [X] T024 [US2] E2E: `tests/FundingPlatform.Tests.E2E/Tests/Notifications/ConfirmDialogMigrationTests.cs` — for a representative subset (admin disable user, applicant delete item, reviewer return application): modal opens with es-CR copy; confirm executes; cancel + Esc abort with no side effect; reuses `data-testid="confirm-dialog"/"confirm-button"/"cancel-button"`. (SC-004)

**Checkpoint**: US2 independently testable; SC-001 (no native confirm) achievable for these sites.

---

## Phase 5: User Story 3 — AJAX/client-side toasts (Priority: P2)

**Goal**: Client-side outcomes use the toast API; no `window.alert`.

**Independent Test**: Force the "Generar todo" enqueue to fail → error toast via `Notify.error`, no `window.alert`.

- [X] T025 [US3] Replace `alert(...)` at `src/FundingPlatform.Web/wwwroot/js/comparison.js:182` with `window.Notify.error(payload.code || 'Error desconocido al encolar.')`. (FR-003, SC-001)
- [X] T026 [P] [US3] E2E: extend reviewer comparison coverage (or add to `Tests/Notifications/`) to assert the enqueue-error path renders an error toast and no `window.alert` fires. (SC-001)

**Checkpoint**: US3 testable; `window.alert` eliminated.

---

## Phase 6: User Story 4 — Validation summary toast (Priority: P2)

**Goal**: Inline field validation stays; failed submit also raises exactly one summary toast.

**Independent Test**: Submit an invalid form → inline field errors AND exactly one "Corrige los campos marcados" toast.

- [X] T027 [US4] Add a ModelState-invalid hook: in `_NotificationToasts.cshtml` (or a `_ValidationSummaryToast.cshtml` it includes), when `ViewContext.ViewData.ModelState.IsValid == false` on a full-page render, emit exactly one sticky error toast "Corrige los campos marcados" (`data-testid="validation-summary-toast"`), de-duplicated against an explicit `ValidationErrors` toast. (FR-008)
- [X] T028 [P] [US4] E2E: submit a form (e.g. Application/Edit) with an invalid required field → assert inline field error present AND exactly one summary toast; valid submit → no summary toast. (SC-005)

**Checkpoint**: US4 testable.

---

## Phase 7: User Story 5 — Accessibility & non-blocking presentation (Priority: P3)

**Goal**: Toasts announced via live region; confirm modal traps focus, Esc cancels, focus returns; no layout shift.

**Independent Test**: Screen reader announces success (polite) and error (assertive); keyboard focus trapped in modal and restored on close.

- [X] T029 [US5] Verify/adjust `aria-live` (polite success/info, assertive warning/error) + `role` on toasts in `_ToastContainer`/`notifications.js`; confirm `confirm-dialog.js` returns focus to the trigger on close and Esc cancels; verify toast overlay causes no layout shift. (FR-012, FR-013, NFR-003)
- [X] T030 [P] [US5] E2E: `Tests/Notifications/` accessibility checks — live-region attributes present on toasts; modal focus returns to trigger on cancel/close; integrate with existing `Brand/AxeContrastTests` patterns if useful. (SC-006)

**Checkpoint**: US5 testable.

---

## Phase 8: Polish & Cross-Cutting

- [X] T031 [P] Coverage verification: grep confirms no `window.alert`/`window.confirm` remain in the affected flows (SC-001) and the banner `.alert` blocks are removed from both layouts (SC-002); confirm `_AutosaveIndicator`, `_StageCountdownBanner`, and `_LookupRejected` were NOT modified (FR-009); capture the result.
- [X] T032 [P] es-CR copy pass over all new toast/modal strings + `data-confirm-*` bodies — no English-only strings (FR-010).
- [X] T033 Update the lone class-based banner assertion `tests/FundingPlatform.Tests.E2E/Fixtures/AuthenticatedTestBase.cs:313` (`.alert-success`) to the toast `data-testid`, and sweep any other PageObject banner helpers that broke.
- [ ] T034 Run the FULL E2E suite via AspireFixture and confirm it is green (Constitution III, SC-007) — personally executed; not a partial run.

---

## Dependencies & Execution Order

- **Phase 1 (Setup)** → **Phase 2 (Foundational)** must complete before any user story.
- **US1 (P1)** and **US2 (P1)** are independent of each other (US1 = toast bridge; US2 = confirm migration) and form the MVP; both depend only on Foundational.
- **US3 (P2)** depends on Foundational toast API (T002); independent of US1/US2.
- **US4 (P2)** depends on Foundational toast API + US1 bridge partial (T008).
- **US5 (P3)** depends on US1 (toasts) + US2 (modal) being in place.
- **Phase 8** runs last; T034 (full E2E green) is the delivery gate.

## Parallel Opportunities

- Foundational: T002, T003, T004, T005 in parallel (distinct files); T006 then T007 sequential (T007 wires what T006 creates).
- US2: T014, T015, T018, T019, T020, T023 in parallel (distinct files); T016/T017/T021/T022 each touch a single multi-site file (do per-file).
- E2E test tasks (T013, T026, T028, T030) parallel with each other once their story code lands.

## Implementation Strategy

MVP = Phase 1 + 2 + US1 + US2 (consistent toasts everywhere + styled confirms). Ship/validate that increment, then layer US3 (AJAX), US4 (validation summary), US5 (a11y), then Phase 8 polish and the mandatory full green E2E run.
