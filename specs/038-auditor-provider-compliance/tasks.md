---
description: "Task list — Spec 038 Auditor role + provider regulatory compliance model"
---

# Tasks: Auditor Role + Provider Regulatory Compliance Model

**Input**: Design documents from `/specs/038-auditor-provider-compliance/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/interfaces.md, quickstart.md

**Tests**: Included — Constitution Principle III makes E2E non-negotiable; the spec has per-story acceptance
scenarios. Delivery bar (CLAUDE.md): filtered/affected E2E must be green, not the full suite.

**Organization**: By user story (US1 P1 MVP → US4 P3). Foundational phase is the shared schema/domain/role
prerequisite that blocks all stories.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: parallelizable (different file, no incomplete dependency)
- **[Story]**: US1–US4 for story-phase tasks only

## Path conventions

4-layer solution: `src/FundingPlatform.{Domain,Application,Infrastructure,Web}`, `src/FundingPlatform.Database`,
`tests/FundingPlatform.Tests.{Unit,Integration,E2E}`.

---

## Phase 1: Setup

- [ ] T001 Confirm baseline build is green on branch `038-auditor-provider-compliance` (`dotnet build FundingPlatform.slnx`) before changes.

---

## Phase 2: Foundational (Blocking Prerequisites)

**⚠️ CRITICAL**: All user stories depend on this phase. It contains the schema change, the entity/enum model,
the role rename, the shared audit + display infrastructure, and the forced PDF fix. The solution must build
green at the checkpoint.

- [ ] T002 [P] Add Domain enums `HaciendaStatus`, `CcssStatus`, `SicopStatus` (`: byte`, codes per data-model.md §Enums) in `src/FundingPlatform.Domain/Enums/HaciendaStatus.cs`, `CcssStatus.cs`, `SicopStatus.cs`.
- [ ] T003 [P] Add Domain `RegulatoryReviewSource : byte` (Manual/Api/System) + `RegulatoryField : byte` (Hacienda/Ccss/Sicop) + `RegulatoryChange` value object (Field discriminator incl. Pme/Warning, OldValue, NewValue, Kind {Changed, ReviewedNoChange}, Source) in `src/FundingPlatform.Domain/Enums/` and `src/FundingPlatform.Domain/ValueObjects/RegulatoryChange.cs`.
- [ ] T004 Modify `Supplier` entity in `src/FundingPlatform.Domain/Entities/Supplier.cs`: remove `HasElectronicInvoice`/`IsCompliant{CCSS,Hacienda,SICOP}`; add the 3 nullable status props + per-field `*LastReviewedAt/By/Source` + `IsPmeOrPyme` + `HasWarning` + `WarningNote` + `RowVersion`; add `ApplyRegulatoryEdit(...)` (sets last-reviewed on changed fields, returns `IReadOnlyList<RegulatoryChange>`) and `ConfirmRegulatoryReviewed(field, actor, now)` (throws if status null — D9); narrow `EditByAdmin` to name-only.
- [ ] T005 Update dacpac `src/FundingPlatform.Database/Tables/dbo.Suppliers.sql`: drop the 4 BIT compliance columns; add the 3 status TINYINTs + 9 per-field reviewed-at/by/source columns + `IsPmeOrPyme BIT`/`HasWarning BIT`/`WarningNote NVARCHAR(1000)` + `RowVersion ROWVERSION`; add 3 `FK_Suppliers_*ReviewedBy_AspNetUsers` (NO ACTION).
- [ ] T006 Update EF `src/FundingPlatform.Infrastructure/Persistence/Configurations/SupplierConfiguration.cs`: remove the 4 bool mappings; map the 3 status enums + 3 sources via `HasConversion<byte?>()`; configure `RowVersion` via `.IsRowVersion()`; map the new scalar columns.
- [ ] T007 Add `supplier.*` action constants (`SupplierRegulatoryChanged`, `SupplierRegulatoryReviewed`, `SupplierPmeChanged`, `SupplierWarningChanged`) + `TargetTypeSupplier = "supplier"` in `src/FundingPlatform.Domain/Entities/AdminAuditEvent.cs`.
- [ ] T008 Route the `supplier.` prefix → `(TargetTypeSupplier, <supplierId>)` (real id, not "0") in `AdminAuditEventWriter.DeriveTarget` (`src/FundingPlatform.Infrastructure/Audit/AdminAuditEventWriter.cs`).
- [ ] T009 [P] Add es-CR copy phrases for the 4 `supplier.*` actions in `AdminAuditEventCopyProvider` (`src/FundingPlatform.Application/Services/AdminAuditEventCopyProvider.cs` — confirm path) so the admin "Actividad reciente" feed renders them.
- [ ] T010 Rename role `SupplierAdmin` → `Auditor` across all inventoried code sites (research D1): roles array + demo user (`supplieradmin@…` → `auditor@programa-semilla.test`, role `Auditor`) in `Identity/IdentityConfiguration.cs`; `SupplierAdminOnlyAttribute`/`SupplierAdminDeniedAttribute` role constants; `AdminUserRole` enum value; the 13 `[Authorize(Roles="Admin,SupplierAdmin")]` attributes; `User.IsInRole("SupplierAdmin")` checks (`HomeController`, `AdminSuppliersController`); `UserAdministrationService` constant + validation message; `AccountController` role-display map + `AssignRole` dev-seam allowlist; `StatusVisualMap`. Keep filter class names + supplier-list DTO names (describe the screen, not the role).
- [ ] T011 Replace `src/FundingPlatform.Database/PostDeployment/03_SeedSupplierAdminRole.sql` with an idempotent rename-or-create of the `Auditor` role (update existing `SUPPLIERADMIN` row's Name/NormalizedName, else insert `AUDITOR`).
- [ ] T012 Repoint the funding-agreement PDF off the dropped booleans: update `Views/FundingAgreement/Partials/_SupplierVerificationPage.cshtml` + the `SupplierCompliance` projection in `FundingAgreementController.BuildDocumentViewModelAsync` to read the new statuses (map status→label) so the PDF build keeps working.
- [ ] T013 [P] Add shared display helpers: `RegulatoryStatusLabels` (verbatim-label lookup + `SelectListItem` builders, blank = "sin revisar") and `ReviewFreshness.Describe(at, byName, source)` (es-CR relative recency) in `src/FundingPlatform.Application/Suppliers/` (or `Web/Helpers/`).

**Checkpoint**: `dotnet build FundingPlatform.slnx` green; dacpac deploys; `Auditor` role active with members preserved.

---

## Phase 3: User Story 1 — Auditor manages provider regulatory compliance (Priority: P1) 🎯 MVP

**Goal**: Auditor sets enumerated Hacienda/CCSS/SICOP statuses + PME/PYME on a provider via dropdowns; the
electronic-invoice control is gone; values persist.

**Independent Test**: Sign in as `auditor@programa-semilla.test`, open a provider, confirm no e-invoice control,
set the three statuses + PME, save, reload → persisted; confirm Auditor reaches only `/Admin/Suppliers*`.

- [ ] T014 [US1] Add `ISupplierComplianceService` + `EditSupplierComplianceCommand` (incl. `Name`, the 3 statuses, `IsPmeOrPyme`, `HasWarning`, `WarningNote`, `ActorUserId`, `RowVersion`) + `SupplierComplianceResult` in `src/FundingPlatform.Application/Suppliers/Compliance/`.
- [ ] T015 [US1] Implement `SupplierComplianceService.EditComplianceAsync` in `src/FundingPlatform.Infrastructure/Services/SupplierComplianceService.cs`: load supplier, set name + `ApplyRegulatoryEdit(...)`, write one `AdminAuditEvent` per returned change, single `SaveChangesAsync`; map `DbUpdateConcurrencyException`/not-found/validation → es-CR `SupplierComplianceResult`. Register in DI (`Infrastructure/DependencyInjection.cs`).
- [ ] T016 [US1] Update `AdminSupplierDetailViewModel` + `AdminEditSupplierViewModel` (`src/FundingPlatform.Web/ViewModels/Admin/AdminSupplierDetailViewModel.cs`): replace the 4 bools with the 3 nullable status enums + `IsPmeOrPyme` + `HasWarning` + `[MaxLength(1000)] WarningNote` + per-field freshness fields + `byte[] RowVersion`.
- [ ] T017 [US1] Rework `AdminSuppliersController` (`Edit` POST → bind `EditSupplierComplianceCommand` → `EditComplianceAsync`; `Detail` GET → populate new VM incl. freshness + RowVersion) in `src/FundingPlatform.Web/Controllers/Admin/AdminSuppliersController.cs`.
- [ ] T018 [US1] Update `Views/Admin/Suppliers/Detail.cshtml`: remove the "Factura electrónica" toggle; replace the 3 compliance checkboxes with `<select>` dropdowns (verbatim Spanish, blank="sin revisar", `data-searchable` per spec 031 if >7 — these are ≤8 so threshold-exempt); add PME/PYME toggle; hidden `RowVersion`; preserve/refresh `data-testid`s.
- [ ] T019 [P] [US1] Add es-CR strings (status field headings, PME label, save/concurrency messages) to `src/FundingPlatform.Web/Resources/AdminSuppliersResources.cs`.
- [ ] T020 [P] [US1] Unit tests in `tests/FundingPlatform.Tests.Unit`: `Supplier.ApplyRegulatoryEdit` (changed field sets last-reviewed; unchanged untouched; empty-change no-op) + `RegulatoryStatusLabels` (verbatim values, null→"sin revisar").
- [ ] T021 [US1] Integration test in `tests/FundingPlatform.Tests.Integration` (real DB): `EditComplianceAsync` persists the 3 statuses + PME and writes the expected `supplier.regulatory_changed`/`supplier.pme_changed` audit rows.
- [ ] T022 [US1] E2E `AuditorProviderComplianceTests` in `tests/FundingPlatform.Tests.E2E`: auditor edits statuses + PME, no e-invoice control present, persists on reload; auditor role is scoped to `/Admin/Suppliers*` (other `/Admin/*` blocked). Provision via `/Account/SeedUser` + `/Account/AssignRole?role=Auditor`.

**Checkpoint**: US1 fully functional + independently testable (MVP).

---

## Phase 4: User Story 2 — Regulatory changes auditable & freshness visible (Priority: P2)

**Goal**: Changes are audited; each status shows last-reviewed recency; auditor can re-confirm a value without
changing it.

**Independent Test**: Change a status → audit row + "revisado hoy"; use "Confirmar revisión" → timestamp
advances, value unchanged, audit row kind=reviewed; control disabled when status unset.

- [ ] T023 [US2] Add `ConfirmReviewedAsync(supplierId, field, actorUserId, rowVersion, ct)` to `ISupplierComplianceService` (`src/FundingPlatform.Application/Suppliers/Compliance/ISupplierComplianceService.cs`) + impl in `src/FundingPlatform.Infrastructure/Services/SupplierComplianceService.cs` (calls `Supplier.ConfirmRegulatoryReviewed`, writes one `supplier.regulatory_reviewed` audit, commits; guard-null surfaces es-CR).
- [ ] T024 [US2] Add `POST /Admin/Suppliers/{supplierId}/ConfirmReviewed` action (binds `field` + `RowVersion`) in `AdminSuppliersController`.
- [ ] T025 [US2] `Views/Admin/Suppliers/Detail.cshtml`: per-field freshness line (`ReviewFreshness.Describe`) + a "Confirmar revisión" button per status, disabled/hidden when the status is unset (D9).
- [ ] T026 [US2] Surface per-field freshness on the reviewer-facing supplier/quote render during application review (the partial pinned in T031).
- [ ] T027 [P] [US2] Unit tests: `ReviewFreshness.Describe` (hoy / hace N días / sin revisar / source suffix); `Supplier.ConfirmRegulatoryReviewed` (refreshes timestamp; throws on null status).
- [ ] T028 [US2] Integration test: `ConfirmReviewedAsync` refreshes timestamp with value unchanged + writes reviewed audit; a value change writes an audit with correct old/new.
- [ ] T029 [US2] E2E `AuditorRegulatoryFreshnessTests`: change status shows freshness; confirm-no-change refreshes; control disabled when unset; admin activity feed shows `supplier.*` events.

**Checkpoint**: US1 + US2 work independently.

---

## Phase 5: User Story 3 — Provider warnings highlight providers during review (Priority: P2)

**Goal**: Auditor flags a provider with a note; reviewers/auditors see it during review; reviewers can't author
it; it never blocks.

**Independent Test**: Auditor sets warning + note; reviewer opens an application using that provider → warning +
note shown, not editable by reviewer, application still advances.

- [ ] T030 [US3] `Views/Admin/Suppliers/Detail.cshtml`: add the warning flag toggle + note textarea (≤1000) to the auditor edit form (flows through `EditComplianceAsync` warning handling — entity/service/VM already cover it from Phase 2/US1).
- [ ] T031 [US3] Pin the reviewer review supplier/quote render partial(s) (grep the quote/supplier render starting from `Views/Application/Review.cshtml` + the spec-020 comparison surface); add a shared `_SupplierWarningBanner` (+ compliance/freshness) partial and render it on each reviewer review site. Reviewers get read-only display only.
- [ ] T032 [P] [US3] es-CR warning copy in `src/FundingPlatform.Web/Resources/AdminSuppliersResources.cs` (warning label, note placeholder, reviewer banner heading).
- [ ] T033 [US3] Unit/Integration: warning change is audited (`supplier.warning_changed`); warning normalize (flag off clears the note; trim; ≤1000 enforced).
- [ ] T034 [US3] E2E `ProviderWarningTests`: auditor sets warning; reviewer sees warning+note, cannot edit; the application still advances (non-blocking).

**Checkpoint**: US1 + US2 + US3 independently functional.

---

## Phase 6: User Story 4 — Auditors notified when a provider is created (Priority: P3)

**Goal**: Provider creation emails all auditors with identifying info + a link, best-effort, allowlist-honored.

**Independent Test**: Applicant creates a supplier → every auditor receives an email (smtp4dev) with name, legal
id, created time, creator, link; send failure does not block creation.

- [ ] T035 [US4] Add `IProviderCreatedNotifier` in `src/FundingPlatform.Application/Suppliers/Notifications/IProviderCreatedNotifier.cs`.
- [ ] T036 [US4] Implement `ProviderCreatedNotifier` in `src/FundingPlatform.Infrastructure/Suppliers/ProviderCreatedNotifier.cs`: resolve all `Auditor`-role users (`Roles.NormalizedName == "AUDITOR"` join), render the template, send one message per auditor via the **Notifications-path `IEmailSender`** (allowlist-wrapped); compose absolute `/Admin/Suppliers/{id}` link; best-effort (catch+log, never throw). Register in DI.
- [ ] T037 [P] [US4] Create email template `src/FundingPlatform.Web/Views/Emails/Suppliers/ProviderCreatedAuditor.cshtml` (es-CR, text-only wordmark, tokens `{{ProviderName}}`/`{{ProviderLegalId}}`/`{{CreatedAt}}`/`{{CreatedByName}}`/`{{ReviewLink}}`).
- [ ] T038 [US4] Trigger the notifier after a successful `SaveChangesAsync` in `src/FundingPlatform.Infrastructure/Services/CreateSupplierBranchHandler.cs`, inside try/catch (FR-024 — must not block creation).
- [ ] T039 [US4] E2E `ProviderCreatedNotificationTests`: applicant creates a supplier → auditor receives the email via `MailCaptureClient.WaitForAsync(filter: ToAddresses.Contains(auditorEmail))`; assert subject + link; confirm a non-allowlisted address would be dropped (allowlist honored).

**Checkpoint**: all four stories independently functional.

---

## Phase 7: Polish & Cross-Cutting

- [ ] T040 [P] Grep `src/` + `tests/` for leftover `"SupplierAdmin"` / `SUPPLIERADMIN` literals; clean stragglers (keep intentionally-retained filter class + DTO names).
- [ ] T041 [P] Update CLAUDE.md demo-seed list (`supplieradmin@…` → `auditor@programa-semilla.test`) and any supplier-compliance references; record in `specs/038-auditor-provider-compliance/` evolution notes if deviations arose.
- [ ] T042 Run `specs/038-auditor-provider-compliance/quickstart.md` walkthrough; ensure the funding-agreement PDF renders with the new statuses (build + PDF carve-out check if `scripts/` has one).
- [ ] T043 Run filtered E2E (`AuditorProviderCompliance|AuditorRegulatoryFreshness|ProviderWarning|ProviderCreatedNotification`) + any affected supplier/admin-suppliers regression classes; confirm green per the delivery bar.

---

## Dependencies & Execution Order

- **Setup (T001)** → **Foundational (T002–T013)** blocks everything. Within Foundational: T002/T003 before T004; T004 before T005/T006; T007 before T008; role-rename T010/T011 independent of the schema tasks but required before US1; T012 must land with T004/T005 (or the build breaks); T013 independent.
- **US1 (T014–T022)** after Foundational. **MVP stops here.**
- **US2 (T023–T029)** after US1 (extends the same service/VM/view).
- **US3 (T030–T034)** after US1 (warning fields are already in the entity/service/VM from Phase 2/US1; this adds UI + reviewer surface). Largely independent of US2; T026 (US2) and T031 (US3) touch the same reviewer partial — sequence T031 before T026 or merge.
- **US4 (T035–T039)** after Foundational (needs the `Auditor` role + a supplier-creation path); independent of US1–US3.
- **Polish (T040–T043)** last.

### Parallel opportunities

- Foundational: T002, T003, T009, T013 in parallel (distinct files); T004/T005/T006 sequential on the model.
- US1: T019, T020 in parallel with T015–T018 wiring.
- US4 is fully parallelizable against US1–US3 once Foundational is done (different files).

## Implementation Strategy

**MVP** = Phase 1 + Phase 2 + Phase 3 (US1): the Auditor role + enumerated compliance + e-invoice removal +
PME — the core conceptual change. Validate, then layer US2 (audit/freshness), US3 (warnings), US4
(notification) incrementally. Commit after each task or logical group (constitution commit discipline).

## Notes

- Schema is dacpac-first; no EF migrations. Dev auto-deploys; Azure prod publish uses `--no-drop` (dropping the
  4 BIT columns there must be handled deliberately — quickstart watch-out).
- Reviewer warning/freshness partial (T031) must be pinned by grep before writing — candidate sites listed in
  research D13.
- Slice-D caveat (research D6): the Hacienda API job's automated actor has no `AspNetUsers` row; not a slice-A
  concern but don't design the audit payload in a way that blocks it.
