---
description: "Task list for 027 Review & Funding-Agreement UX Refinements"
---

# Tasks: Review & Funding-Agreement UX Refinements

**Input**: Design documents from `specs/027-review-funding-ux/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/

**Tests**: INCLUDED — constitution principle III makes Playwright E2E non-negotiable for every user story; targeted unit/integration tests are added where the design calls for them (US1 fallback, US4 projection, US5 write).

**Organization**: By user story, in priority order. P1 = US1, US2, US4. P2 = US5, US3, US6, US7, US8. No DB schema change in any task; the PDF document body is never modified (FR-009).

## Format: `[ID] [P?] [Story] Description`

- **[P]**: parallelizable (different files, no incomplete dependency)
- **[Story]**: US1..US8 (story phases only)

---

## Phase 1: Setup

**Purpose**: Establish a clean baseline so regressions are attributable.

- [ ] T001 Confirm baseline green: `dotnet build FundingPlatform.slnx` and a baseline run of `tests/FundingPlatform.Tests.E2E`; note any pre-existing failures before changes.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Shared bits consumed by multiple stories.

**⚠️ CRITICAL**: Complete before US5/US6/US7 begin.

- [X] T002 [P] Create shared required-field marker partial `src/FundingPlatform.Web/Views/Shared/_RequiredMark.cshtml` rendering `<span class="text-danger" aria-label="campo obligatorio">*</span>` (consumed by US5 field, US6 sweep, US7 forms).

**Checkpoint**: shared marker available.

---

## Phase 3: User Story 1 - Generator shown by name, not GUID (Priority: P1) 🎯 MVP

**Goal**: The funding-agreement page attributes generation to a display name, never a GUID.

**Independent Test**: Generate as a known user; FA page shows their name; empty name → email; deleted account → stable fallback, never a GUID.

- [X] T003 [P] [US1] In `src/FundingPlatform.Application/.../SignedUploadService.cs:154`, replace `GeneratedByDisplayName: agreement?.GeneratedByUserId` with a value resolved via `IUserStoreReader.GetDisplayNameAsync` (inject `IUserStoreReader` into the service if not already present).
- [X] T004 [P] [US1] In `src/FundingPlatform.Application/.../FundingAgreementService.cs:70`, apply the same display-name resolution.
- [X] T005 [US1] Unit test the fallback ladder (full name → email → stable fallback; deleted account does not throw and yields no GUID) in `tests/FundingPlatform.Tests.Unit`.
- [X] T006 [US1] E2E: generate an agreement, open `/Applications/{id}/FundingAgreement`, assert the "Generado — … por X" line shows a name and matches no GUID pattern, in `tests/FundingPlatform.Tests.E2E`.

**Checkpoint**: US1 independently demoable.

---

## Phase 4: User Story 2 - Confirm before executing/rejecting the signed convenio (Priority: P1)

**Goal**: Aprobar (execute) and Rechazar require an explicit confirmation stating the consequence before committing.

**Independent Test**: Click each action → consequence-stated confirm appears; dismiss = no change; confirm = action; reject still requires its comment.

- [X] T007 [US2] Add `data-confirm` attributes to the Aprobar and Rechazar submit buttons in `src/FundingPlatform.Web/Views/Applications/_FundingAgreementPanel.cshtml:127-151` per `contracts/ui-surfaces.md` (Aprobar → variant `statelocking`, body "Esto ejecuta el convenio."; Rechazar → variant `destructive`, body "Esto rechaza la carga; el solicitante podrá enviar otra.").
- [X] T008 [US2] Verify the reject mandatory-comment UX still holds with the confirm interception (recommendation: keep server-side enforcement at `FundingAgreementController.Reject` as the backstop; if the confirm fires before HTML5 validation, gate the confirm on a non-empty comment). Adjust the panel/JS hook only if needed — no change to `confirm-dialog.js`.
- [X] T009 [US2] E2E: confirm modal appears for both actions; dismiss leaves state unchanged; confirm performs the action; reject without a comment is still blocked. In `tests/FundingPlatform.Tests.E2E`.

**Checkpoint**: US2 independently demoable.

---

## Phase 5: User Story 4 - Consistent detailed decision summary across all five screens (Priority: P1) 🎯 CORE

**Goal**: One shared per-line summary (line code, product, category, technical specs, supplier, amount + CRC note, status; rejected lines also reason + all quoted suppliers) rendered identically on all five surfaces. PDF document unchanged.

**Independent Test**: Walk one application (≥1 approved, ≥1 rejected, a non-CRC quote) through all five screens; each shows the identical field set.

- [X] T010 [P] [US4] Create `src/FundingPlatform.Application/DTOs/DecisionSummaryLineDto.cs` with `DecisionSummaryLineDto` + `DecisionSummaryQuotationView` per `contracts/decision-summary.md`.
- [X] T011 [US4] Create `src/FundingPlatform.Application/Services/IDecisionSummaryProjection.cs` + `DecisionSummaryProjection.cs` implementing the mapping rules (approved/rejected/pending; rejected lists all quotes; conversion note lifted from `FundingAgreementController.BuildConversionNote`); register in DI.
- [X] T012 [P] [US4] Unit tests for the projection (approved supplier+amount; rejected reason + all quotes; pending status; non-CRC conversion note; ordering by LineCode then Id) in `tests/FundingPlatform.Tests.Unit`.
- [X] T013 [P] [US4] Create read-only shared partial `src/FundingPlatform.Web/Views/Shared/_DecisionSummary.cshtml` (`@model IReadOnlyList<DecisionSummaryLineDto>`) per the partial contract; es-CR status badges.
- [X] T014 [US4] Render `_DecisionSummary` on `src/FundingPlatform.Web/Views/ApplicantResponse/Index.cshtml` and feed it from `ApplicantResponseController.Index` (this adds the missing technical specifications).
- [X] T015 [US4] Render `_DecisionSummary` on `src/FundingPlatform.Web/Views/FundingAgreement/Details.cshtml`, replacing the approved-only preview; feed it from `FundingAgreementController` (covers the generate / signing / signed-review states).
- [X] T016 [US4] Render `_DecisionSummary` read-only alongside the existing capture UI on `src/FundingPlatform.Web/Views/Review/Review.cshtml` (capture controls unchanged).
- [X] T017 [US4] E2E: a single five-screen parity test asserting the same field set (incl. technical specs) on reviewer review, applicant accept/reject, FA Details (generate/sign/signed-review states), for both an approved and a rejected line. In `tests/FundingPlatform.Tests.E2E`.

**Checkpoint**: US4 independently demoable; US3 may now reuse the partial.

---

## Phase 6: User Story 5 - Reviewer-assigned applicant code on the first review screen (Priority: P2)

**Goal**: Reviewers/admins set `CodigoPersonal` of the application's applicant from `/Review/{id}`; read-only on the applicant profile.

**Independent Test**: Reviewer sets the code → persists → visible read-only on that applicant's profile; not editable there.

- [X] T018 [US5] Add `POST /Review/{id:int}/ApplicantCode` to `src/FundingPlatform.Web/Controllers/ReviewController.cs` (`[Authorize(Roles="Reviewer,Admin")]`, `[ValidateAntiForgeryToken]`, group-overlap auth mirroring the existing review predicate; resolve applicant via `application.Applicant.UserId`; set `CodigoPersonal` ≤40 chars via `UserManager<ApplicationUser>.FindByIdAsync`+`UpdateAsync`; es-CR success TempData).
- [X] T019 [US5] Add the "Código del solicitante" input + save control to `src/FundingPlatform.Web/Views/Review/Review.cshtml`, prefilled with the current value, required-marked via `_RequiredMark`, posting to the new action.
- [X] T020 [US5] Integration test (real DB, no mocks): reviewer POST sets `CodigoPersonal` on the applicant's `ApplicationUser`; non-owning/non-overlapping reviewer is rejected. In `tests/FundingPlatform.Tests.Integration`.
- [X] T021 [US5] E2E: reviewer sets the code on `/Review/{id}` → log in as that applicant → code shows read-only on the profile. In `tests/FundingPlatform.Tests.E2E`.

**Checkpoint**: US5 independently demoable; US3 may now display the code.

---

## Phase 7: User Story 3 - Richer applicant detail on the funding-agreement page (Priority: P2)

**Goal**: FA page shows company, representative, legal id + type, email, phone, applicant code, group, submission date. Screen-only.

**Independent Test**: Open the FA page for an applicant with all fields populated → all render; empty optional → "—"; PDF document unchanged.

**Depends on**: US4 (partial already on Details), US5 (code value).

- [X] T022 [US3] Extend the FA Details view model + `FundingAgreementController` to populate an applicant block: company, representative name, legal id + identification type (spec-026 formatting), email, phone, `CodigoPersonal`, group (spec 016), submission date; neutral "—" for empty optionals.
- [X] T023 [US3] Render the applicant block on `src/FundingPlatform.Web/Views/FundingAgreement/Details.cshtml`.
- [X] T024 [US3] E2E: FA page shows all applicant fields; an empty optional renders "—"; assert the generated PDF document body is unchanged (no new applicant detail leaks into it). In `tests/FundingPlatform.Tests.E2E`.

**Checkpoint**: US3 independently demoable.

---

## Phase 8: User Story 6 - Consistent required-field markers on every form (Priority: P2)

**Goal**: Every required field on every form uses the shared `_RequiredMark`; optional fields none.

**Independent Test**: Sampled required field per form area shows the marker + aria-label; optional fields show none.

- [X] T025 [P] [US6] Sweep applicant forms to use `_RequiredMark` (replace ad-hoc markers; add where only HTML5 `required`): `Account/Register.cshtml`, `Account/ChangePassword.cshtml`, `Account/ForgotPassword.cshtml`, `Account/ResetPassword.cshtml`, `Account/Profile.cshtml`, `Application/Edit.cshtml`, `Application/Impact.cshtml`, `Supplier/Add.cshtml`, `ApplicantResponse/*` (under `src/FundingPlatform.Web/Views/`).
- [X] T026 [P] [US6] Sweep admin forms similarly: `Admin/Users/Create.cshtml`, `Admin/Users/Edit.cshtml`, `Admin/CreateTemplate.cshtml`, `Admin/EditTemplate.cshtml`, `Admin/ExchangeRates/Create.cshtml`, `Admin/Plantillas/Create.cshtml`, `Admin/Plantillas/Edit.cshtml`, `Admin/Configuration.cshtml`, `Admin/PublicLanding/Index.cshtml`.
- [X] T027 [US6] Sweep the reviewer form `Review/Review.cshtml` (including the US5 código field) to use `_RequiredMark`.
- [X] T028 [US6] E2E: assert the marker (with `aria-label="campo obligatorio"`) is present on a sampled required field in an applicant, an admin, and the reviewer form, and absent on a known optional field. In `tests/FundingPlatform.Tests.E2E`.

**Checkpoint**: US6 independently demoable.

---

## Phase 9: User Story 7 - HTML tooltips on applicant fields (Priority: P2)

**Goal**: Info icon beside each applicant field; hover shows an HTML-capable tooltip; es-CR copy.

**Independent Test**: Each applicant field has an info icon; hover renders formatted HTML (not escaped); leave dismisses; no copy → no icon.

- [X] T029 [P] [US7] Create a static es-CR hint-copy provider `src/FundingPlatform.Web/Resources/HintCopy.cs` (key → es-CR HTML string) for the applicant field set (research D6); author first-pass copy.
- [X] T030 [US7] Extend `src/FundingPlatform.Web/Views/Shared/_HintTooltip.cshtml`: render a `ti ti-info-circle` info icon carrying the copy (`data-hint`), and make `ResolveCopy()` read from `HintCopy` instead of returning null; render copy HTML-safely; render nothing when no copy.
- [X] T031 [P] [US7] Create `src/FundingPlatform.Web/wwwroot/js/hint-tooltip.js` (own JS, no `window.bootstrap`): on mouseover/focus of `[data-hint]` show an HTML bubble, hide on mouseout/blur; register the script in `src/FundingPlatform.Web/Views/Shared/_Layout.cshtml` after `confirm-dialog.js`.
- [X] T032 [US7] Decorate applicant view-model properties with `[Hint("…")]` and wire `_HintTooltip` next to each applicant field in `Account/Register.cshtml`, `Application/Edit.cshtml`, `Application/Impact.cshtml`, `Supplier/Add.cshtml`.
- [X] T033 [US7] E2E: an applicant field shows an info icon; hovering renders the tooltip with formatted HTML content. In `tests/FundingPlatform.Tests.E2E`.

**Checkpoint**: US7 independently demoable.

---

## Phase 10: User Story 8 - Restructure the left sidebar into grouped sections (Priority: P2)

**Goal**: Sidebar regrouped into Inicio / Administración / Proceso with zero removals and role-gating preserved.

**Independent Test**: Per role, every prior destination still reachable under the right group; three group headers present for admin; supplier-admin-only variant unchanged.

- [ ] T034 [US8] Restructure the sidebar data + render loop in `src/FundingPlatform.Web/Views/Shared/_Layout.cshtml` per `contracts/sidebar-structure.md`: keep Inicio + reviewer/applicant items top-level; group Administración (Empresas proveedoras, Plantillas base, Reportes, Monedas, Tipos de cambio, Usuarios, Configuración); add a "Proceso" section header (`data-section-testid="proceso-section"`) over Grupos, Plantillas de impacto, Cotizaciones pendientes. Preserve every `AllowedRoles` and the supplier-admin-only variant; render no empty group header.
- [ ] T035 [US8] Add the "Starters" nav entry under Proceso linking to the existing applications listing filtered by Process (deep link to the Reports "Applications" view with a `processId` filter per the confirmed recommendation; add the minimal route/query support if the tab is not yet URL-addressable). No new standalone surface.
- [ ] T036 [US8] E2E: per-role (admin/reviewer/applicant/supplier-admin) assert every prior sidebar destination is still reachable and grouped correctly, the three group headers render for admin, and Starters opens the applications listing. In `tests/FundingPlatform.Tests.E2E`.

**Checkpoint**: US8 independently demoable.

---

## Phase 11: Polish & Cross-Cutting

- [ ] T037 es-CR copy pass over all new/changed strings (confirm tooltips, confirm-dialog bodies, status badges, success messages — no English leaks).
- [ ] T038 Run `specs/027-review-funding-ux/quickstart.md` manual verification end to end.
- [ ] T039 Run the FULL E2E suite (`tests/FundingPlatform.Tests.E2E`) and confirm green — the delivery gate (SC-008, constitution III). Personally execute; partial/structural runs do not count.

---

## Dependencies & Execution Order

### Phase order
- Setup (P1 tasks) → Foundational (T002) → US1 → US2 → US4 → US5 → US3 → US6 → US7 → US8 → Polish.
- P1 stories (US1, US2, US4) deliver the MVP and can be demoed before P2 work.

### Story dependencies
- **US1, US2**: independent (after Setup).
- **US4**: independent core; produces the shared partial.
- **US5**: independent; needs `_RequiredMark` (T002) for its field marker.
- **US3**: depends on **US4** (partial on Details) and **US5** (code value to display).
- **US6**: needs `_RequiredMark` (T002); otherwise independent.
- **US7**: needs `_RequiredMark` only if its forms’ required fields are part of the sweep; otherwise independent.
- **US8**: independent.

### Parallel opportunities
- T003/T004 (US1, different files) in parallel.
- T010, T012, T013 (US4: DTO, unit test, partial) in parallel before wiring T014–T016.
- T025/T026 (US6 applicant vs admin sweeps, different files) in parallel.
- T029/T031 (US7 copy provider vs JS, different files) in parallel.
- Whole stories can be staffed in parallel after Foundational, except US3 waits on US4+US5.

---

## Implementation Strategy

### MVP (P1)
1. Setup + Foundational.
2. US1 → US2 → US4. Validate each independently. This is a demoable MVP (name fixed, actions guarded, consistent decision summary).

### Incremental (P2)
3. US5 → US3 (US3 reuses US4 partial + US5 code) → US6 → US7 → US8. Each independently testable.

### Gate
4. Polish + **full E2E green** before declaring delivery (SC-008).

---

## Notes
- No DB schema change anywhere; no EF migrations (constitution IV).
- PDF document body never modified (FR-009 / spec 018).
- es-CR copy inline or via static provider; no `IStringLocalizer` (NFR-003).
- Interaction JS is own-JS (no `window.bootstrap`).
- Commit after each task or logical group (constitution commit discipline).
- E2E must drive the real UI journey (no deep-link shortcuts), per project convention.
