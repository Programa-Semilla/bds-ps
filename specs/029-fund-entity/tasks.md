# Tasks: Fund (Fondo) Entity

**Input**: Design documents from `specs/029-fund-entity/`
**Prerequisites**: plan.md, spec.md (evolved 2026-06-10), research.md (D1–D10), data-model.md, contracts/ui-and-routes.md, quickstart.md

**Tests**: INCLUDED — Constitution III makes Playwright E2E non-negotiable, and every user story defines an Independent Test. Each story phase ends with its E2E class plus targeted unit/integration tests that must hit a real DB (no mocks — CLAUDE.md).

**Organization**: By user story (priority order). Story priorities from spec.md: US6 (P1, anchor — foundational prerequisite for US4/US5), US1 (P1), US2 (P1), US3 (P2), US4 (P2), US5 (P3).

## Path Conventions

Clean Architecture web app: `src/FundingPlatform.{Domain,Application,Infrastructure,Web,Database}`, `tests/FundingPlatform.Tests.{Unit,Integration,E2E}`.

---

## Phase 1: Setup

- [x] T001 Confirm branch `029-fund-entity` builds green as a baseline: `dotnet build FundingPlatform.slnx`.

---

## Phase 2: Foundational (Blocking Prerequisites)

**⚠️ CRITICAL**: Schema + Domain + EF + storage wiring below block ALL user stories. The required `Processes.FundId` and `Applications.GroupId` columns mean the app cannot create Processes/Applications until US2 and US6 wire the values — so Foundational + US6 + US1 + US2 together restore a working create path.

### Schema (dacpac — Constitution IV)

- [x] T002 [P] Create `src/FundingPlatform.Database/Tables/dbo.Funds.sql` per data-model.md (columns, `PK_Funds`, `UX_Funds_Name`).
- [x] T003 Add `FundId INT NOT NULL`, `CONSTRAINT FK_Processes_Funds ... ON DELETE NO ACTION`, and `IX_Processes_FundId` to `src/FundingPlatform.Database/Tables/dbo.Processes.sql`.
- [x] T004 Add `GroupId INT NOT NULL`, `CONSTRAINT FK_Applications_Groups ... ON DELETE NO ACTION`, and `IX_Applications_GroupId` to `src/FundingPlatform.Database/Tables/dbo.Applications.sql`.
- [x] T005 Create `src/FundingPlatform.Database/PostDeployment/0X_SeedFunds.sql` (idempotent MERGE: upsert `Fondo General` Active → capture id → set `FundId` on seed Processes → ensure seed Applications have `GroupId`); reference it in the post-deploy entry script BEFORE the Process/Group seeds.

### Domain (Constitution II)

- [x] T006 [P] Create `src/FundingPlatform.Domain/Enums/FundStatus.cs` (`Active = 0`, `Archived = 1`).
- [x] T007 [P] Create `src/FundingPlatform.Domain/Exceptions/FundArchivedException.cs`.
- [x] T008 Create `src/FundingPlatform.Domain/Entities/Fund.cs` aggregate with factory + behavior methods (`Create`, `Rename`, `EditDescription`, `Archive`, `Reactivate`, `SetRegulation`, `RemoveRegulation`, `HasRegulation`) and invariants per data-model.md (depends on T006).
- [x] T009 [P] Add `fund.create/edit/archive/reactivate/regulation.set/regulation.remove` action constants + `fund` target-type constant to `src/FundingPlatform.Domain/Entities/AdminAuditEvent.cs`.
- [x] T010 Edit `src/FundingPlatform.Domain/Entities/Process.cs`: add `FundId`, `Fund` nav; extend `Create` to take `fundId`; add `SetFund(int)` guarded against Closed.
- [x] T011 Edit `src/FundingPlatform.Domain/Entities/Application.cs`: add `GroupId`, `Group` nav, `bool IsFrozen` (service-fed), and throw `FundArchivedException` from each applicant/reviewer-facing mutating method when frozen (depends on T007).

### Application + Infrastructure wiring

- [x] T012 [P] Add `FundRegulation` ([Description("fund-regulations")]) to `src/FundingPlatform.Application/Abstractions/Storage/FileCategory.cs`; add a `FundRegulation` category to `StorageCategoriesOptions` + the `For()` switch in `StorageOptions.cs` (MaxSizeBytes 20 MiB, UrlExpirySeconds 300, RetentionPolicy "none"); add `Storage:Categories:FundRegulation:*` defaults to `appsettings`.
- [x] T013 [P] Add `IQueryable<Application> ExcludeArchivedFund(IQueryable<Application> source)` to `src/FundingPlatform.Application/Abstractions/IApplicationQueryFilter.cs`.
- [x] T014 Create `src/FundingPlatform.Infrastructure/Persistence/Configurations/FundConfiguration.cs` (table, name/desc lengths, status `HasConversion<byte>`, `UX_Funds_Name`, RowVersion, one-to-many to Process) (depends on T008).
- [x] T015 Edit `ProcessConfiguration.cs`: map `Fund` FK (`HasOne(p=>p.Fund).WithMany(f=>f.Processes).HasForeignKey(p=>p.FundId).OnDelete(NoAction)`) (depends on T010).
- [x] T016 Edit `ApplicationConfiguration.cs`: map `Group` FK (`HasOne(a=>a.Group).WithMany().HasForeignKey(a=>a.GroupId).OnDelete(NoAction)`) (depends on T011).
- [x] T017 Register `DbSet<Fund>` in the EF `DbContext` and apply `FundConfiguration`.
- [x] T018 Implement `ExcludeArchivedFund` in `src/FundingPlatform.Infrastructure/Persistence/ApplicationQueryFilter.cs` (`source.Where(a => a.Group.Process.Fund.Status != FundStatus.Archived)`) (depends on T013, T016).
- [x] T019 Foundational checkpoint: `dotnet build FundingPlatform.slnx` green; dacpac deploys cleanly via `dotnet run --project src/FundingPlatform.AppHost` (new objects created, seed Fund present).

**Checkpoint**: schema + domain + EF ready. User stories can proceed.

---

## Phase 3: User Story 6 - Anchor each application to its Fund at creation (Priority: P1) 🎯 MVP-critical

**Goal**: Every application is anchored to exactly one Group (→ Process → Fund) at creation; Plantilla resolution becomes deterministic.

**Independent Test**: Applicant with one eligible group auto-anchors; with several must choose; with none is blocked; submit validation uses the anchored Process's Plantilla.

- [x] T020 [US6] Add `GroupId` to `CreateApplicationCommand`; in `src/FundingPlatform.Application/Services/ApplicationService.cs` resolve the applicant's eligible groups (member of group whose `Process.Status==Active` AND `Process.Fund.Status==Active`), validate the chosen `GroupId` against that set, and set `Application.GroupId`.
- [x] T021 [US6] In `src/FundingPlatform.Web/Controllers/ApplicationController.cs` (`Create` GET/POST) + `CreateApplicationViewModel`: add required `GroupId`; implement 0-eligible (block with es-CR message), 1-eligible (auto, hidden), ≥2 (required select) logic.
- [x] T022 [US6] Update `src/FundingPlatform.Web/Views/Application/Create.cshtml` to render the Process/convocatoria selector (labelled by Process name; Group when ambiguous) and the blocked-state message.
- [x] T023 [US6] Replace the nondeterministic group-membership Plantilla lookup with the anchor in `src/FundingPlatform.Infrastructure/Services/GetApplicationReviewProjection.cs` (`ResolveMinimumQuotationsAsync`) and `src/FundingPlatform.Infrastructure/Services/SubmitApplicationHandler.cs` (use `application.Group.Process.Plantilla`).
- [x] T024 [US6] Ensure seed Applications carry a valid `GroupId` (extend T005 seed or demo-seed) so existing E2E create/submit flows pass.
- [x] T025 [P] [US6] Unit tests in `tests/FundingPlatform.Tests.Unit`: eligible-group resolution (0/1/many), invalid-group rejection, Plantilla-via-anchor determinism.
- [x] T026 [US6] E2E `ApplicationFundAnchorTests` in `tests/FundingPlatform.Tests.E2E` (POM): auto-anchor, choose, blocked-no-group, submit uses anchored Plantilla.

**Checkpoint**: applications anchor deterministically; create/submit work again end-to-end.

---

## Phase 4: User Story 1 - Administer Funds (Priority: P1) 🎯 MVP

**Goal**: Admin Fund catalog with create/edit/archive/reactivate and regulation upload/replace/remove.

**Independent Test**: Create Fund (no PDF) → Active in list; upload/replace/remove PDF; reject non-PDF/dup-name/blank; audit rows written.

- [x] T027 [US1] Create `src/FundingPlatform.Application/Abstractions/IFundService.cs` + command records (Create/Edit/Archive/Reactivate/SetRegulation/RemoveRegulation).
- [x] T028 [US1] Implement `src/FundingPlatform.Infrastructure/Services/FundService.cs`: CRUD + lifecycle; unique-name pre-check (es-CR); regulation store/replace/remove via `IObjectStorage` (ObjectKey.Build with `FileCategory.FundRegulation`, delete superseded blob); write `AdminAuditEvent` for each mutation (depends on T008, T012, T027).
- [x] T029 [P] [US1] Create `src/FundingPlatform.Web/ViewModels/Admin/AdminFundViewModels.cs` (Index rows + status filter, Create, Edit, Details with Processes list).
- [x] T030 [P] [US1] Create `src/FundingPlatform.Web/Resources/AdminFundsResources.cs` (all es-CR strings, NFR-004).
- [x] T031 [US1] Implement `src/FundingPlatform.Web/Controllers/Admin/AdminFundsController.cs` (`/Admin/Funds`): Index(status filter), Create GET/POST, Edit POST, Details, Regulation POST + Remove POST, Archive POST, Reactivate POST; `[Authorize(Roles="Admin")]` + `[SupplierAdminDenied]`; `[UploadSizeGuard(FileCategory.FundRegulation)]` + `%PDF-` magic-byte validation; TempData toasts (depends on T028, T029, T030).
- [x] T032 [US1] Create `src/FundingPlatform.Web/Views/Admin/Funds/{Index,Create,Edit,Details}.cshtml` (status badge, spec-024 `data-confirm` on archive/reactivate/remove, regulation upload control, Processes list on Details).
- [x] T033 [P] [US1] Add sidebar entry `new("funds","Fondos","/Admin/Funds","ti ti-coin", new[]{"Admin"})` to `src/FundingPlatform.Web/Views/Shared/_Layout.cshtml`.
- [x] T034 [US1] Register `IFundService`/`FundService` in DI composition root.
- [x] T035 [P] [US1] Unit tests: Fund domain behavior (archive/reactivate idempotency, regulation set/remove all-or-nothing, name/description validation).
- [x] T036 [US1] Integration tests (real DB): FundService create/edit/archive persistence, unique-name violation, audit-row emission, regulation columns set/cleared.
- [x] T037 [US1] E2E `FundAdminCrudTests`: create (no PDF), upload/replace/remove PDF, reject non-PDF + dup-name + blank, archive/reactivate, audit visible.

**Checkpoint**: Fund catalog fully usable by admins.

---

## Phase 5: User Story 2 - Associate every Process with a Fund (Priority: P1) 🎯 MVP

**Goal**: Process create/edit requires an Active Fund; Process list shows Fund + filters by it.

**Independent Test**: Create Process blocked without Fund; selector lists only Active Funds; reassign to another Active Fund; list column + filter work.

- [x] T038 [US2] In `src/FundingPlatform.Web/Controllers/Admin/AdminProcessesController.cs` + `AdminProcessCreateViewModel`: add required `FundId` (Active-only dropdown), validate (reject missing/Archived with es-CR), and support reassign on edit; add `?fundId=` filter to Index.
- [x] T039 [US2] Update `src/FundingPlatform.Web/Views/Admin/Processes/Create.cshtml` with the required Fund selector.
- [x] T040 [US2] Update `src/FundingPlatform.Web/Views/Admin/Processes/Index.cshtml` with a Fund column + Fund filter dropdown (alongside the existing ProcessStatus filter).
- [x] T041 [US2] Wire `Process.Create(fundId)` / `SetFund` through the Process create/edit service path (`ProcessService`).
- [x] T042 [US2] E2E `ProcessRequiresFundTests`: create blocked without Fund; Active-only selector; reassign; list filter/column.

**Checkpoint**: every Process belongs to a Fund; admin can pivot the Process list by Fund.

---

## Phase 6: User Story 3 - Applicant downloads the governing regulation (Priority: P2)

**Goal**: Applicant downloads a Fund's regulation in the context of a Process under it (Active + present).

**Independent Test**: Active Fund with PDF → applicant downloads; remove PDF → link gone.

- [x] T043 [US3] Implement applicant regulation download (`src/FundingPlatform.Web/Controllers/FundRegulationController.cs` or action): resolve via `IObjectStorage.ResolveServingHandleAsync(..., BackendStream)`, gated on Fund Active + regulation present, `File(stream,"application/pdf",name)`; 404 otherwise.
- [x] T044 [US3] Render the regulation download link conditionally on the applicant Process/application surface (only when Fund Active + regulation exists).
- [ ] T045 [US3] E2E `RegulationDownloadTests`: download when Active+present; no link when absent.

**Checkpoint**: regulation reaches applicants.

---

## Phase 7: User Story 4 - Archive a Fund to freeze its activity (Priority: P2)

**Goal**: Archiving immediately hides + freezes all anchored applications for non-admins; reactivation restores.

**Independent Test**: Archive Fund → its applications vanish from applicant list/reviewer queue/signing inbox and reject mutations; admin still sees via filter; reactivate restores.

- [x] T046 [US4] Compose `ExcludeArchivedFund` alongside `ExcludeDeleted` at every non-admin read site: `ApplicationRepository` (`GetByApplicantIdAsync`, `GetForApplicantDashboardAsync`, `GetByStateForReviewerAsync`, `GetPendingAgreementPagedAsync`, `ApplicantSharesAnyGroupAsync`), `ReviewerDashboardProjection`, reviewer-facing counters in `AdminDashboardCountersReader`, `StageExpiryReminderService` (do NOT apply to admin reports) (depends on T018).
- [x] T047 [US4] Add a controller freeze-guard helper and apply it to applicant/reviewer mutations: `ApplicationController` (Create target-fund check, Edit, AddItem, RemoveItem, Autosave, Submit, Remove/Withdraw, Impact) and `QuotationController` (Add, Edit) — return es-CR error toast when the application's Fund is Archived; admin exempt.
- [x] T048 [US4] Feed `Application.IsFrozen` from the loaded `Group.Process.Fund.Status` in the service layer and ensure domain mutating methods throw `FundArchivedException` (defense-in-depth with T011).
- [x] T049 [US4] Integration tests (real DB): each non-admin read surface excludes archived-Fund applications; reactivation makes them reappear.
- [x] T050 [P] [US4] Unit tests: frozen application rejects each mutating domain method; admin path unaffected.
- [ ] T051 [US4] E2E `FundArchiveFreezeTests`: archive hides+freezes across applicant/reviewer/signing surfaces; mutation rejected; reactivate restores.

**Checkpoint**: archive force-freeze fully enforced.

---

## Phase 8: User Story 5 - Filter Processes and reports by Fund (Priority: P3)

**Goal**: Exact Fund filter/column on existing admin reports + CSV (Process list filter already shipped in US2); Fund detail lists Processes (shipped in US1 Details).

**Independent Test**: Filter Applications/Funded Items/Aging by Fund → only that Fund's rows; Fund column in table + CSV.

- [x] T052 [US5] Add `int? FundId` to `ListApplicationsRequest`, `ListFundedItemsRequest`, `ListAgingApplicationsRequest`; add `FundName` to `ApplicationRowDto`, `FundedItemRowDto`, and the aging row DTO.
- [x] T053 [US5] In `src/FundingPlatform.Infrastructure/Persistence/Reports/ReportQueryService.cs`: add the filter clause `a.Group.Process.FundId == req.FundId` and project `FundName` via the anchor in the relevant base queries.
- [x] T054 [US5] Add the Fund column to CSV header + row lines in `AdminReportsService` export methods.
- [x] T055 [US5] Add a Fund `<select>` (all Funds incl. Archived) to `Views/Admin/Reports/{Applications,FundedItems,Aging}.cshtml` and bind `FundId` in `AdminReportsController`.
- [x] T056 [US5] E2E `FundReportFilterTests`: report filter limits rows; Fund column present in table + CSV.

**Checkpoint**: reports pivot by Fund exactly.

---

## Phase 9: Polish & Cross-Cutting

- [ ] T057 [P] es-CR review of all new strings (resources + inline) for tone/consistency (spec 012 / NFR-004).
- [ ] T058 [P] Verify no CDN/new managed deps introduced; regulation assets served locally; run any relevant `scripts/` budget checks.
- [ ] T059 Run `dotnet test tests/FundingPlatform.Tests.Unit` and `tests/FundingPlatform.Tests.Integration` — all green.
- [ ] T060 Run the FULL `tests/FundingPlatform.Tests.E2E` suite and confirm green (CLAUDE.md delivery bar — feature is not delivered until this passes).
- [ ] T061 [P] Refresh `specs/029-fund-entity/` open items (OI-1/2/3 resolutions) and the brainstorm overview if decisions changed.

---

## Dependencies & Execution Order

- **Phase 1 → Phase 2** must complete before any story.
- **Story order**: US6 (P1, anchor) → US1 (P1) → US2 (P1) → US3 (P2) → US4 (P2) → US5 (P3).
  - US6 unblocks a working create path and is a prerequisite for **US4** (freeze derives Fund via the anchor) and **US5** (reports derive Fund via the anchor).
  - US3 depends on US1 (regulation must exist) and US2 (Process↔Fund context).
  - US4 depends on US6 (anchor) + US1 (archive lifecycle) + T018.
  - US5 depends on US6 (anchor) + US1/US2 (Funds + Process association).
- **MVP = Phase 1 + Phase 2 + US6 + US1 + US2** (the full P1 set): a working Fund hierarchy with required association, anchored applications, and admin catalog. US3/US4/US5 are additive increments.

## Parallel Opportunities

- Foundational: T002, T006, T007, T009, T012, T013 are `[P]` (distinct files) and can run together; T008→T014, T010→T015, T011→T016 are sequential pairs.
- US1: T029, T030, T033, T035 `[P]`. US6: T025 `[P]`. US4: T050 `[P]`. Polish: T057, T058, T061 `[P]`.
- Within a story, tests (`[P]`) can be authored alongside the implementation tasks they cover.

## Implementation Strategy

Deliver the P1 MVP first (Foundational → US6 → US1 → US2), verifying each story's Independent Test, then layer US3 (download), US4 (freeze), US5 (reports). Treat T060 (full E2E green) as the non-negotiable completion gate.
