# Tasks: Full Reconciliation Engine (spec 048 / financial-execution P4)

**Input**: Design documents from `specs/048-full-reconciliation-engine/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/interfaces.md, quickstart.md

**Tests**: INCLUDED — the constitution makes E2E per user story non-negotiable (Principle III). Unit tests cover the pure evaluator + aggregate transitions; integration tests cover real-SQL materialization + indexes; E2E covers each story's golden + error paths.

**Organization**: by user story. US1+US2 form the deliverable spine (land together per plan phasing); US3 and US4 are additive increments. Keep the P1–P3 money-gate regression (SC-004) green at every checkpoint.

## Format: `[ID] [P?] [Story] Description`
- **[P]**: parallelizable (different file, no dependency on an incomplete task)
- **[Story]**: US1–US4; Setup/Foundational/Polish carry no story label

---

## Phase 1: Setup & Foundational (Blocking Prerequisites)

**Purpose**: schema, enums, entities, EF mapping — nothing below can start until these land. No behavior yet.

- [X] T001 [P] Add `DiscrepancyState : byte` enum (Open/Assigned/UnderCorrection/Resolved/Waived) in `src/FundingPlatform.Domain/Enums/DiscrepancyState.cs` with XML doc citing spec 048 + the TINYINT `HasConversion<byte>()` note
- [X] T002 [P] Add `DiscrepancyScopeType : byte` enum (Document/Payment/BudgetLine/Participant/Tranche) in `src/FundingPlatform.Domain/Enums/DiscrepancyScopeType.cs`
- [X] T003 [P] Extend `DiscrepancySeverity` with `Warning = 1` in `src/FundingPlatform.Domain/Enums/DiscrepancySeverity.cs` (update the doc-comment that reserved the P4 seam)
- [X] T004 [P] Extend `ReconciliationComparison` with `EvidenceDateAnomaly=5`, `PossibleDuplicatePayment=6`, `GraphInvoiceAllocationDrift=7` in `src/FundingPlatform.Domain/Enums/ReconciliationComparison.cs`
- [X] T005 Create `DiscrepancyEvent` append-only child entity in `src/FundingPlatform.Domain/Entities/DiscrepancyEvent.cs` (internal ctor + static factories, no mutators; copy `DisbursementLedgerEntry`)
- [X] T006 Create `Discrepancy` aggregate root in `src/FundingPlatform.Domain/Entities/Discrepancy.cs` (owns `_events`; `Detect` factory; guarded transitions `Refresh`/`AutoResolve`/`AutoReopen`/`Assign`/`MarkUnderCorrection`/`Waive` — `Waive` throws on Blocking + blank reason; `RowVersion`; copy `Evidence`) — depends on T001–T005
- [X] T007 [P] Create `WarningDescriptor` value object in `src/FundingPlatform.Domain/ValueObjects/WarningDescriptor.cs`
- [X] T008 [P] Add `DiscrepancyConfiguration` in `src/FundingPlatform.Infrastructure/Persistence/Configurations/DiscrepancyConfiguration.cs` (`.HasConversion<byte>()` on all 4 enum props; `.IsRowVersion()`; `PropertyAccessMode.Field` on `Events`; unique `UX_Discrepancies_Identity`; `IX_Discrepancies_App_State`; filtered `IX_Discrepancies_Assignee`; FK NO ACTION to Applications + AspNetUsers) — depends on T006
- [X] T009 [P] Add `DiscrepancyEventConfiguration` in `src/FundingPlatform.Infrastructure/Persistence/Configurations/DiscrepancyEventConfiguration.cs` (byte enums `.HasConversion<byte>()`; FK CASCADE to Discrepancies, NO ACTION to AspNetUsers; `IX_DiscrepancyEvents_Discrepancy`) — depends on T005
- [X] T010 Add two `DbSet`s (`Discrepancies`, `DiscrepancyEvents`) to `src/FundingPlatform.Infrastructure/Persistence/AppDbContext.cs` in the finance section — depends on T006, T005
- [X] T011 [P] Add `dbo.Discrepancies.sql` in `src/FundingPlatform.Database/Tables/dbo.Discrepancies.sql` (TINYINT cols + defaults, DECIMAL(18,2), DATETIMEOFFSET(0), ROWVERSION; NO ACTION FKs; `UX_Discrepancies_Identity`; `CK_Discrepancies_Waive_Blocking`; `CK_Discrepancies_WaivedReason`; query index)
- [X] T012 [P] Add `dbo.DiscrepancyEvents.sql` in `src/FundingPlatform.Database/Tables/dbo.DiscrepancyEvents.sql` (CASCADE FK to Discrepancies, NO ACTION FK to AspNetUsers, timeline index)
- [X] T013 Add `DiscrepancyEnumMaterializationTests` in `tests/FundingPlatform.Tests.Integration/` proving all 4 TINYINT enums round-trip on real SQL (mirror `DisbursementEnumMaterializationTests`) + the `UX_Discrepancies_Identity` unique constraint + CASCADE delete — depends on T008–T012

**Checkpoint**: schema deploys; entities materialize on real SQL; no behavior yet.

---

## Phase 2: User Story 1 — Persisted discrepancies with severity (Priority: P1)

**Goal**: every mutation detects + persists the full discrepancy set with fixed severity; blocking still blocks, warnings don't; the per-application surface shows persisted rows.

**Independent test**: `ReconciliationPersistenceTests` (quickstart) — blocking persisted + blocks validate; clean → none; each of the 3 warnings persisted + non-blocking.

- [X] T014 [P] [US1] Create pure `ReconciliationWarnings` evaluator in `src/FundingPlatform.Domain/Services/ReconciliationWarnings.cs` (3 static methods: `EvaluateEvidenceDateAnomalies`, `EvaluatePossibleDuplicatePayments`, `EvaluateGraphInvoiceAllocationDrift`; primitive input records; returns `WarningDescriptor`s) — depends on T007
- [X] T015 [P] [US1] Unit tests `ReconciliationWarningsTests` in `tests/FundingPlatform.Tests.Unit/` (each rule: positive, negative, boundary at 0.01; date-anomaly both directions) — depends on T014
- [X] T016 [US1] Add `IReconciliationMaterializer` in `src/FundingPlatform.Application/Reconciliation/IReconciliationMaterializer.cs` (`MaterializeAsync(applicationId, actorUserId, ct)`)
- [X] T017 [US1] Implement `ReconciliationMaterializer` in `src/FundingPlatform.Infrastructure/Services/ReconciliationMaterializer.cs`: read the app's disbursements/lines/evidence; call the existing `DisbursementReconciliation`/`DisbursementLineReconciliation` legs + `ReconciliationWarnings`; map each output → (scope, comparison, severity, expected/actual); diff against persisted rows by stable identity → `Detect` new / `Refresh` present (fixed map comparison→severity); batched reads (no N+1); own SaveChanges — depends on T016, T014, T006
- [X] T018 [US1] Register `IReconciliationMaterializer` in `src/FundingPlatform.Infrastructure/DependencyInjection.cs`
- [X] T019 [US1] Wire `MaterializeAsync` into `DisbursementService` (after the domain SaveChanges in Record/Edit/Validate/Cancel + line commit/uncommit) in `src/FundingPlatform.Infrastructure/Services/DisbursementService.cs` — verify the money gate's own fresh throw-path is untouched (FR-004) — depends on T017
- [X] T020 [P] [US1] Wire `MaterializeAsync` into `EvidenceService` (attach/replace/delete/allocate) in `src/FundingPlatform.Infrastructure/Services/EvidenceService.cs` — depends on T017
- [X] T021 [P] [US1] Wire `MaterializeAsync` into `BudgetLineClosureService` (close/reopen) in `src/FundingPlatform.Infrastructure/Services/BudgetLineClosureService.cs` — depends on T017
- [X] T022 [P] [US1] Extend `DisbursementResources.ComparisonLabel` for comparisons 5–7 + add `SeverityLabel`/`SeverityBadge` (text+icon, never color-alone) in `src/FundingPlatform.Web/Resources/DisbursementResources.cs`
- [X] T023 [US1] Update `src/FundingPlatform.Web/Views/Disbursement/_DiscrepancyList.cshtml` to bind the persisted discrepancy rows (severity badge, lifecycle state; the `/Reconciliation/{id}` deep-link renders as plain text until US3 wires the route in T043) + adjust the `DisbursementDetail` read path to source persisted rows — depends on T017, T022
- [X] T024 [US1] Integration test `ReconciliationMaterializerTests` in `tests/FundingPlatform.Tests.Integration/` (real SQL: insert-on-new, refresh-keeps-identity, severity mapping, batched-read no-N+1) — depends on T017
- [X] T025 [US1] E2E `ReconciliationPersistenceTests` in `tests/FundingPlatform.Tests.E2E/` (5 scenarios per quickstart: blocking-blocks, clean, duplicate-warning-non-blocking, date-anomaly, graph-drift) — depends on T019–T023

**Checkpoint A**: discrepancies persist with severity; blocking blocks, warnings don't; P1–P3 regression green.

---

## Phase 3: User Story 2 — Discrepancy lifecycle with correction history (Priority: P1)

**Goal**: assign / mark-under-correction / waive (warnings only, reason+audit); auto-resolve on fix + auto-reopen on recurrence; per-discrepancy timeline; concurrency-safe.

**Independent test**: `DiscrepancyLifecycleTests` (quickstart) — assign persists across re-run; auto-resolve+reopen; waive warning; cannot-waive-blocking; waived reopens on amount change; concurrency refusal.

- [X] T026 [US2] Add auto-resolve / auto-reopen to `ReconciliationMaterializer`: cleared identity → `AutoResolve` (system-sentinel actor), recurring Resolved/Waived → `AutoReopen`; append `DiscrepancyEvent`s in the materializer SaveChanges in `src/FundingPlatform.Infrastructure/Services/ReconciliationMaterializer.cs` — depends on T017
- [X] T027 [P] [US2] Add `discrepancy.*` action constants + `TargetTypeDiscrepancy` to `src/FundingPlatform.Domain/Entities/AdminAuditEvent.cs`
- [X] T028 [P] [US2] Add the `discrepancy.` prefix branch (extract `discrepancyId`) to `src/FundingPlatform.Infrastructure/Audit/AdminAuditEventWriter.cs` — depends on T027
- [X] T029 [P] [US2] Add `DiscrepancyReasons` es-CR refusal strings (CannotWaiveBlocking / ReasonRequired / Concurrency / NotFound) in `src/FundingPlatform.Application/Reconciliation/DiscrepancyReasons.cs`
- [X] T030 [US2] Add `IDiscrepancyLifecycleService` + `DiscrepancyActionResult` in `src/FundingPlatform.Application/Reconciliation/IDiscrepancyLifecycleService.cs`
- [X] T031 [US2] Implement `DiscrepancyLifecycleService` in `src/FundingPlatform.Infrastructure/Services/DiscrepancyLifecycleService.cs` (Assign/MarkUnderCorrection/Waive → aggregate method + `DiscrepancyEvent` + `discrepancy.*` audit via two-SaveChanges; catch `DbUpdateConcurrencyException` → Concurrency; no manual Resolve/Reopen) — depends on T030, T027–T029, T006
- [X] T032 [US2] Register `IDiscrepancyLifecycleService` in `src/FundingPlatform.Infrastructure/DependencyInjection.cs`
- [X] T033 [P] [US2] Unit tests for `Discrepancy` transitions in `tests/FundingPlatform.Tests.Unit/` (waive-blocking throws; reason-required; auto-resolve/reopen state math; waived-reopens-on-amount-change) — depends on T006
- [X] T034 [US2] Integration test `DiscrepancyLifecycleServiceTests` in `tests/FundingPlatform.Tests.Integration/` (real SQL: assign→timeline row+audit; concurrency via RowVersion; auto-resolve after fix) — depends on T031, T026
- [X] T035 [US2] E2E `DiscrepancyLifecycleTests` in `tests/FundingPlatform.Tests.E2E/` (6 scenarios per quickstart) driven through the per-application discrepancy surface + a Development-only test seam that invokes `IDiscrepancyLifecycleService` (no dependency on the US3 dashboard UI — that path is re-exercised by T044) — depends on T031, T026

**Checkpoint B (spine complete)**: US1+US2 delivered together; full lifecycle + history; P1–P3 regression green.

---

## Phase 4: User Story 3 — Reconciliation dashboard (Priority: P2)

**Goal**: group→agency dashboard with summary tiles + FR-023 filters + detail timeline; FinOp group-scoped, Admin agency-wide, Auditor read-only.

**Independent test**: `ReconciliationDashboardTests` (quickstart) — scoping (A/B/Admin/Auditor); filters; detail; accessibility; money-gate race.

- [X] T036 [US3] Add `IReconciliationDashboardProjection` + DTOs + `ReconciliationFilter` in `src/FundingPlatform.Application/Reconciliation/IReconciliationDashboardProjection.cs`
- [X] T037 [US3] Implement `ReconciliationDashboardProjection` in `src/FundingPlatform.Infrastructure/Persistence/ReconciliationDashboardProjection.cs` (group-scoped in-query: admin short-circuit, group-overlap on `app.Applicant.UserId`, empty-group early return, `ExcludeDeleted`/`ExcludeArchivedFund`, `MaxRows=500`; summary tiles + list + scope-checked detail; resolve filter dims per scope-type; build-then-filter in-memory) — depends on T036
- [X] T038 [US3] Register the projection in `src/FundingPlatform.Infrastructure/DependencyInjection.cs`
- [X] T039 [P] [US3] Add `ReconciliationResources` es-CR view copy (tiles, filter labels, timeline labels, page headers) in `src/FundingPlatform.Web/Resources/ReconciliationResources.cs`
- [X] T040 [US3] Add `ReconciliationDashboardController` (`[Authorize(Roles="Financial Operator,Admin,Auditor")]`, `[Route("Reconciliation")]`) with Index/Detail + per-discrepancy `GuardWriteAsync` (flat-404 group-scope → 403 read-only) + Assign/UnderCorrection/Waive POSTs delegating to the lifecycle service in `src/FundingPlatform.Web/Controllers/ReconciliationDashboardController.cs` — depends on T037, T031
- [X] T041 [US3] Add views: `Views/Reconciliation/Index.cshtml` (`_KpiTile` summary strip + GET-form filter toolbar + list), `Detail.cshtml` (FR-054 fields + `DiscrepancyEvent` timeline + write affordances gated on `CanWrite`), plus `_SummaryTiles`/`_FilterToolbar` partials in `src/FundingPlatform.Web/Views/Reconciliation/` — depends on T040, T039
- [X] T042 [P] [US3] Add the `reconciliation` sidebar entry to `operativoEntries` (Financial Operator/Admin/Auditor) in the sidebar partial under `src/FundingPlatform.Web/Views/Shared/`
- [X] T043 [US3] Finalize the `_DiscrepancyList` deep-link to `/Reconciliation/{id}` in `src/FundingPlatform.Web/Views/Disbursement/_DiscrepancyList.cshtml` — depends on T040
- [X] T044 [US3] E2E `ReconciliationDashboardTests` in `tests/FundingPlatform.Tests.E2E/` (scoping A/B/Admin/Auditor-read-only; filters; detail+timeline; text+icon accessibility; money-gate-race still blocks) + seed throwaway second group — depends on T040, T041

**Checkpoint C**: dashboard live, correctly scoped; regression green.

---

## Phase 5: User Story 4 — Assignment notification (Priority: P3)

**Goal**: best-effort direct-send email to the assignee on assignment; never on detection; never blocks.

**Independent test**: `DiscrepancyAssignmentNotificationTests` (quickstart) — one mail on assign; none on detection.

- [X] T045 [P] [US4] Add branded email views `Views/Emails/DiscrepancyAssignment.cshtml` + `.text.cshtml` (compose `_EmailLayout` brand partials; es-CR) in `src/FundingPlatform.Web/Views/Emails/`
- [X] T046 [US4] Add `DiscrepancyAssignmentEmailFactory` (mirror `InvitationEmailFactory` + `IEmailViewRenderer`) in `src/FundingPlatform.Infrastructure/Email/DiscrepancyAssignmentEmailFactory.cs` + register in DI — depends on T045
- [X] T047 [US4] Call the factory inline (best-effort, log-and-continue on failure) in `DiscrepancyLifecycleService.AssignAsync` in `src/FundingPlatform.Infrastructure/Services/DiscrepancyLifecycleService.cs` — depends on T046, T031
- [X] T048 [US4] E2E `DiscrepancyAssignmentNotificationTests` in `tests/FundingPlatform.Tests.E2E/` (assign → exactly one branded mail captured via smtp4dev to the allowlisted operator; detection-without-assignment → no mail) — depends on T047

**Checkpoint D**: assignment notification delivered; feature complete.

---

## Phase 6: Polish & Cross-Cutting

- [X] T049 [P] Verify no new N+1 in the materializer and the dashboard projection (batched reads); add the query-count guard used in the P3 completeness projection
- [X] T050 [P] Run the P1–P3 regression filtered E2E (`Disbursement*`/`Tranche*`/`BudgetLine*`/`Evidence*`) and confirm green (SC-004)
- [X] T051 [P] es-CR copy sweep — no English literals in the new views/resources (constitution rule)
- [X] T052 Update `CLAUDE.md` Recent Changes + flip the active-plan pointer to shipped once merged; append the spec-048 revisit outcome to `brainstorm/41-financial-disbursement-platform.md`

---

## Dependencies & Execution Order

- **Phase 1 (Setup/Foundational)** blocks everything. T001–T004 are `[P]`; T005→T006→(T008,T010); T011,T012 `[P]`; T013 last.
- **US1 (Phase 2)** depends on Phase 1. MVP-critical (the materializer). Delivers persisted detection.
- **US2 (Phase 3)** depends on US1's materializer (extends it with auto-resolve/reopen) + the entity transitions from Phase 1. **Land US1+US2 together (spine).**
- **US3 (Phase 4)** depends on US2 (lifecycle service for the POST actions) + the persisted rows. Additive.
- **US4 (Phase 5)** depends on US2 (`AssignAsync`). Additive, lowest priority.
- **Polish (Phase 6)** last.

## Parallel opportunities
- Phase 1: T001–T004 (enums) together; T008/T009 (configs) together; T011/T012 (SQL) together.
- US1: T014+T015 (evaluator+its tests) alongside T022 (resources); T020/T021 (evidence/closure wiring) parallel once T017 lands.
- US2: T027/T028/T029/T033 parallel.
- US3: T039/T042 parallel with projection/controller work.

## MVP scope
**US1 + US2** (the spine) = a viable MVP: discrepancies are persisted, severity-tiered, and fully manageable through their lifecycle with history — even before the dashboard (US3) and notification (US4) land. Deliver the spine, verify its filtered E2E + the P1–P3 regression, then add US3 and US4 as increments.

## Task count
52 tasks — Setup/Foundational 13, US1 12, US2 10, US3 9, US4 4, Polish 4.
