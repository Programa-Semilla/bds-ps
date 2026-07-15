---
description: "Task list for Financial Disbursement Core (spec 045)"
---

# Tasks: Financial Disbursement Core

**Input**: Design documents from `specs/045-financial-disbursement-core/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/interfaces.md, quickstart.md

**Tests**: INCLUDED and REQUIRED. Constitution III makes Playwright E2E per user story the non-negotiable primary gate; the pure reconciliation evaluator gets unit tests; ledger/projection/enum-materialization get real-SQL integration tests (CLAUDE.md: integration hits a real DB, never mocks).

**Organization**: Grouped by user story (US1–US5 from spec.md) after a shared Setup + Foundational base. US1 is the MVP checkpoint.

**Delivery gate**: filtered E2E for the `Disbursement*` classes green (not the full ~30-min suite).

## Format: `[ID] [P?] [Story] Description with file path`

- **[P]**: parallelizable (different files, no dependency on an incomplete task)
- **[Story]**: US1–US5 (Setup/Foundational/Polish carry no story label)

---

## Phase 1: Setup

- [ ] T001 Confirm branch `045-financial-disbursement-core` and a clean baseline build: `dotnet build FundingPlatform.slnx`
- [ ] T002 [P] Add storage category: `FileCategory.DisbursementEvidence` with `[Description("disbursement-evidence")]` + `ContainerName`/`AllContainerNames` in `src/FundingPlatform.Application/Abstractions/Storage/FileCategory.cs`, and a `StorageCategoryOptions DisbursementEvidence` (`MaxSizeBytes = DefaultMaxSizeBytes20Mib`, `ServingMode.BackendStream`) + `StorageCategoriesOptions.For(...)` switch case in `src/FundingPlatform.Application/Abstractions/Storage/StorageOptions.cs`

---

## Phase 2: Foundational (Blocking Prerequisites)

**⚠️ CRITICAL**: No user story work can begin until this phase is complete. These are shared by all stories.

- [ ] T003 [P] Create 5 `: byte` enums in `src/FundingPlatform.Domain/Enums/`: `DisbursementState` (Recorded=0/Inconsistent=1/Validated=2/Cancelled=3), `EvidenceKind` (BankReceipt=0/Invoice=1), `LedgerEntryType` (Allocation=0/Disbursement=1), `DiscrepancySeverity` (Blocking=0), `ReconciliationComparison` (DisbursementVsBankReceipt=0/DisbursementVsInvoice=1/TotalVsAllocation=2)
- [ ] T004 [P] Create value-object records `ReconciliationDiscrepancy` and `ParticipantBalance` in `src/FundingPlatform.Domain/ValueObjects/` (fields per data-model.md)
- [ ] T005 Create `Disbursement` entity + state machine (`Record`/`EditDetails`/`ApplyReconciliation`/`IsValidatable`/`Validate`/`Cancel`, executed-gate + amount>0 invariants) in `src/FundingPlatform.Domain/Entities/Disbursement.cs` (depends: T003)
- [ ] T006 [P] Create `DisbursementEvidence` entity (`Attach`/`Replace`, kind + amount>0 + CRC invariants, pre-validation gate) in `src/FundingPlatform.Domain/Entities/DisbursementEvidence.cs` (depends: T003)
- [ ] T007 [P] Create `DisbursementLedgerEntry` entity with append-only factories `Allocation(...)` / `ForValidatedDisbursement(...)` and **no mutators** in `src/FundingPlatform.Domain/Entities/DisbursementLedgerEntry.cs` (depends: T003)
- [ ] T008 Create pure static `DisbursementReconciliation.Evaluate(disbursementAmount, bankReceiptAmount?, invoiceAmount?, sumOfNonCancelledIncludingThis, allocation)` (all three comparisons, zero tolerance, ≥1-colón detection, deterministic) in `src/FundingPlatform.Domain/Services/DisbursementReconciliation.cs` (depends: T003, T004)
- [ ] T009 [P] Unit tests for the evaluator in `tests/FundingPlatform.Tests.Unit/DisbursementReconciliationEvaluatorTests.cs`: three comparisons, 1-colón boundary, missing-evidence-is-not-a-discrepancy, over-disbursement difference, exact-match-clean (depends: T008)
- [ ] T010 [P] dacpac table `src/FundingPlatform.Database/Tables/dbo.Disbursements.sql` (Amount `DECIMAL(18,2)` + `CK_Disbursements_Amount_Positive`, State `TINYINT`, FKs Applications/AspNetUsers `NO ACTION`, `RowVersion`, `IX_Disbursements_ApplicationId`, `IX_Disbursements_ApplicationId_State`)
- [ ] T011 [P] dacpac table `src/FundingPlatform.Database/Tables/dbo.DisbursementEvidence.sql` (Kind `TINYINT`, Amount `DECIMAL(18,2)` + positive CK, Currency `CHAR(3)`, FileSize positive CK, FKs `NO ACTION`, `RowVersion`, `UX_DisbursementEvidence_Disbursement_Kind UNIQUE (DisbursementId, Kind)`)
- [ ] T012 [P] dacpac table `src/FundingPlatform.Database/Tables/dbo.DisbursementLedgerEntries.sql` (EntryType `TINYINT`, Amount `DECIMAL(18,2)`, nullable DisbursementId FK `NO ACTION`, `RowVersion`, `UX_DisbursementLedger_Allocation UNIQUE (ApplicationId) WHERE [EntryType]=0`, `UX_DisbursementLedger_Disbursement UNIQUE (DisbursementId) WHERE [EntryType]=1`)
- [ ] T013 [P] EF config `src/FundingPlatform.Infrastructure/Persistence/Configurations/DisbursementConfiguration.cs` (`Amount` → `HasColumnType("decimal(18,2)")`; `State` → **`HasConversion<byte>()`**; `RowVersion` → `IsRowVersion()`; FK `DeleteBehavior.Restrict`; no `Application` nav)
- [ ] T014 [P] EF config `src/FundingPlatform.Infrastructure/Persistence/Configurations/DisbursementEvidenceConfiguration.cs` (`Kind` → `HasConversion<byte>()`; `Amount` decimal(18,2); `Currency` `char(3)` fixed-length; `RowVersion`)
- [ ] T015 [P] EF config `src/FundingPlatform.Infrastructure/Persistence/Configurations/DisbursementLedgerEntryConfiguration.cs` (`EntryType` → `HasConversion<byte>()`; `Amount` decimal(18,2); nullable DisbursementId FK Restrict; `RowVersion`)
- [ ] T016 Add `DbSet`s `Disbursements`, `DisbursementEvidence`, `DisbursementLedgerEntries` to `src/FundingPlatform.Infrastructure/Persistence/AppDbContext.cs` (depends: T005, T006, T007)
- [ ] T017 [P] Application interfaces `IDisbursementService`, `IParticipantBalanceProjection` + commands/DTOs (`RecordDisbursementCommand`, `EditDisbursementCommand`, `AttachDisbursementEvidenceCommand`, `DisbursementListItem`, `DisbursementDetail`, `DisbursementEvidenceDownload`) in `src/FundingPlatform.Application/Disbursements/` (per contracts/interfaces.md)
- [ ] T018 Infrastructure `DisbursementService` + `ParticipantBalanceProjection` skeletons (ctor deps `AppDbContext`, `IObjectStorage`, `IAdminAuditEventWriter`, `ILogger`; using-alias for the domain/namespace collision as in `FundsUsageEvidenceService`) in `src/FundingPlatform.Infrastructure/Services/`, registered in `src/FundingPlatform.Infrastructure/DependencyInjection.cs` (depends: T016, T017)
- [ ] T019 [P] Add `disbursement.*` event constants + `TargetTypeDisbursement = "disbursement"` in `src/FundingPlatform.Domain/Entities/AdminAuditEvent.cs`, and a `DeriveTarget` branch parsing the real disbursement id from payload in `src/FundingPlatform.Infrastructure/Audit/AdminAuditEventWriter.cs`
- [ ] T020 Wire the `"Financial Operator"` group-scoped role (existence only): add to `roles[]` in `src/FundingPlatform.Infrastructure/Identity/IdentityConfiguration.cs`; add to `AllowedRoles` + `SelectPrimaryRole` precedence in `src/FundingPlatform.Infrastructure/Identity/UserAdministrationService.cs` (leave `NormalizeGroupIdsForRole` unchanged; do NOT add to `IsGrouplessRole`); add to `AssignRole` allow-list in `src/FundingPlatform.Web/Controllers/AccountController.cs`; add to `roles[]`+label maps in `src/FundingPlatform.Web/Views/Admin/Users/Create.cshtml` and `Edit.cshtml` with the group selector **shown** (JS `isGroupless` must exclude it — mirror Edit L226, not Create L177); add `new("disbursement-inbox", "Desembolsos", Url.Action("Index","Disbursement"), "ti ti-cash-banknote", new[]{ "Financial Operator","Admin" })` to `operativoEntries` in `src/FundingPlatform.Web/Views/Shared/_Layout.cshtml`
- [ ] T021 dacpac `src/FundingPlatform.Database/PostDeployment/10_SeedFinancialOperatorRole.sql` (idempotent `INSERT ... WHERE NOT EXISTS` on `AspNetRoles` for `Financial Operator`/`FINANCIAL OPERATOR`); add `:r .\10_SeedFinancialOperatorRole.sql` at the tail of `src/FundingPlatform.Database/PostDeployment/SeedData.sql`; add `<Build Remove>` + `<None Include>` entries in `src/FundingPlatform.Database/FundingPlatform.Database.sqlproj`
- [ ] T022 Integration test `tests/FundingPlatform.Tests.Integration/DisbursementEnumMaterializationTests.cs` that inserts + **materializes each TINYINT enum column from real SQL Server** (State/Kind/EntryType) — guards the `Byte→Int32` throw EF-InMemory hides (depends: T010, T011, T012, T013, T014, T015, T016)

**Checkpoint**: schema, entities, evaluator, storage, audit vocab, role, and DI exist. User stories can begin.

---

## Phase 3: User Story US1 — Record, prove, reconcile to the colón (Priority: P1) 🎯 MVP

**Goal**: A Financial Operator records a disbursement, attaches a bank receipt + invoice, sees a to-the-colón discrepancy (or a clean result), corrects it, and validates.

**Independent Test**: On one executed application, drive record → attach (mismatched invoice) → see ₡72 discrepancy + refused Validar → correct → validate. (AC-001, AC-005)

- [ ] T023 [P] [US1] E2E page object `tests/FundingPlatform.Tests.E2E/PageObjects/DisbursementPage.cs` (record form, evidence upload, discrepancy panel, Validar button, state badge)
- [ ] T024 [P] [US1] E2E `tests/FundingPlatform.Tests.E2E/Tests/DisbursementReconciliationTests.cs`: `RecordWithMismatchedInvoice_FlagsColonDiscrepancy_BlocksValidation`, `MissingInvoice_CannotValidate_ShowsMissing`, `CorrectInvoice_ClearsDiscrepancy_AllowsValidation` (reuse `FundingAgreementSeeder.SeedExecutedAgreementAsync`; seed a Financial Operator via `RegisterUserAsync` + `AssignRoleAsync`)
- [ ] T025 [US1] Implement `DisbursementService.RecordAsync` (executed-gate, amount>0/CRC, post the single `Allocation` ledger entry if absent + the disbursement in one `SaveChanges`, run evaluator → persist derived State, audit as `SaveChanges #2`; allocation = Σ `Quotation.ConvertedCrcAmount` of selected line-item quotations) in `src/FundingPlatform.Infrastructure/Services/DisbursementService.cs` (depends: T018)
- [ ] T026 [US1] Implement `AttachEvidenceAsync` in `src/FundingPlatform.Infrastructure/Services/DisbursementService.cs` (create-or-replace per `Kind`, blob-first via `IObjectStorage`, `EvidenceFileTypePolicy.IsAllowed` magic-byte gate, best-effort blob cleanup on row failure, re-run reconciliation) (depends: T025)
- [ ] T027 [US1] Implement `ValidateAsync` in `src/FundingPlatform.Infrastructure/Services/DisbursementService.cs` (gate `IsValidatable` = both evidence present AND zero discrepancies; on pass flip State=Validated + post immutable `Disbursement` ledger entry in one `SaveChanges`; distinct es-CR refusals for missing-evidence vs has-discrepancy) (depends: T026)
- [ ] T028 [US1] Implement `EditAsync` in `src/FundingPlatform.Infrastructure/Services/DisbursementService.cs` (pre-validation guard, re-run reconciliation, surface `DbUpdateConcurrencyException` via the Application-layer string-name filter → es-CR retry message) (depends: T025)
- [ ] T029 [US1] `DisbursementController` in `src/FundingPlatform.Web/Controllers/DisbursementController.cs` — `[Authorize(Roles="Financial Operator,Admin,Auditor")]`, `[Route("Applications/{applicationId:int}/Disbursements")]`; GET `""`/`"{id}"`, POST `"Record"`/`"{id}/Edit"`/`"{id}/Evidence"` (`[UploadSizeGuard(FileCategory.DisbursementEvidence)]`)/`"{id}/Validate"`, GET evidence download; `[ValidateAntiForgeryToken]` on POSTs; executed-state gate + cross-app disbursement-id guard (group-overlap scoping deferred to US5) (depends: T025, T026, T027, T028)
- [ ] T030 [P] [US1] Views + VMs in `src/FundingPlatform.Web/Views/Disbursements/` (`Index.cshtml`, `Detail.cshtml`, `_DisbursementRow.cshtml`, `_DiscrepancyList.cshtml` — discrepancy shown by **icon + text**, not color alone) and `src/FundingPlatform.Web/ViewModels/Disbursements/`
- [ ] T031 [P] [US1] es-CR `src/FundingPlatform.Web/Resources/DisbursementResources.resx` (record/validate/refusal strings; missing-evidence, has-discrepancy, non-CRC, concurrency-retry)
- [ ] T032 [US1] Integration `tests/FundingPlatform.Tests.Integration/DisbursementLedgerTests.cs` (real SQL): validating posts exactly one `Disbursement` ledger entry; the filtered-unique index blocks a second post; the `Allocation` snapshot equals Σ `ConvertedCrcAmount` (depends: T025, T027)

**Checkpoint**: US1 fully functional — the reconciliation walking skeleton is demoable.

---

## Phase 4: User Story US2 — Real-time five-dimension balance (Priority: P2)

**Goal**: Operator/Auditor/Admin see Allocated/Paid/Validated/Pending/Available updating live.

**Independent Test**: Record then validate disbursements against one agreement; assert the five figures at each step (`Available = Allocated − Paid`, unchanged by validation). (SC-004)

- [ ] T033 [P] [US2] E2E `tests/FundingPlatform.Tests.E2E/Tests/ParticipantBalanceTests.cs`: `FiveDimensions_ReconcileExactly_AsDisbursementsRecordedAndValidated`
- [ ] T034 [US2] Implement `ParticipantBalanceProjection.GetForApplicationAsync` (Allocated = ledger Allocation entry else computed Σ `ConvertedCrcAmount`; Validated = Σ ledger Disbursement entries; Pending = Σ Disbursements in {Recorded,Inconsistent}; Paid = Validated+Pending; Available = Allocated−Paid, no clamp) in `src/FundingPlatform.Infrastructure/Services/ParticipantBalanceProjection.cs` (depends: T018, T025)
- [ ] T035 [P] [US2] `_BalanceCard.cshtml` partial + VM, wired into `Views/Disbursements/Index.cshtml` (depends: T030)
- [ ] T036 [US2] Integration `tests/FundingPlatform.Tests.Integration/DisbursementProjectionTests.cs` (real SQL): `Paid = Validated + Pending`, `Available = Allocated − Paid`, validated excluded from Pending (no double-count) (depends: T034)

**Checkpoint**: balance surfaced and reconciles exactly.

---

## Phase 5: User Story US3 — Partial payments & over-disbursement guard (Priority: P3)

**Goal**: Multiple disbursements per agreement; the total may not exceed the allocation; over-disbursement goes blocking and `Available` legibly negative.

**Independent Test**: Two disbursements summing within total succeed (Available→0); a third exceeding it is blocked and `Available` shows negative. (SC-005, FR-020)

- [ ] T037 [P] [US3] E2E `tests/FundingPlatform.Tests.E2E/Tests/DisbursementPartialAndOverTests.cs`: `PartialPayments_SumWithinTotal_Succeed_AvailableToZero`, `OverDisbursement_Blocked_AvailableGoesNegative`
- [ ] T038 [US3] Wire comparison (3) into `RecordAsync`/`EditAsync`: recompute Σ over all non-cancelled disbursements (including the one being written) vs allocation; over → the crossing disbursement becomes `Inconsistent` with an over-disbursement discrepancy (depends: T025, T028)
- [ ] T039 [US3] Render negative `Available` plainly (never clamp to zero, as an over-disbursement signal) in `_BalanceCard.cshtml` + es-CR over-disbursement copy in `DisbursementResources.resx` (depends: T035, T031)

**Checkpoint**: partial + over-disbursement behavior correct.

---

## Phase 6: User Story US4 — Correct-before-validate, lock-after, full audit (Priority: P4)

**Goal**: Pre-validation edit/replace/cancel; validated is locked; every action audited with before/after.

**Independent Test**: Edit + cancel a pending disbursement (allowed); validate another; attempt edit/delete on it (refused); confirm every action is in the audit trail. (SC-006, SC-007)

- [ ] T040 [P] [US4] E2E `tests/FundingPlatform.Tests.E2E/Tests/DisbursementLifecycleTests.cs`: `PreValidation_EditReplaceCancel_Allowed_ReconciliationReruns`, `Validated_EditAndDelete_Refused`, `EveryAction_AppearsInAuditTrail_WithActorAndBeforeAfter`
- [ ] T041 [US4] Implement `CancelAsync` (guard State ∈ {Recorded,Inconsistent}) + controller POST `"{id}/Cancel"` + row action in the views (depends: T029)
- [ ] T042 [US4] Enforce lock-after-validated in `EditAsync`/`AttachEvidenceAsync`/`CancelAsync` (domain guards throw; service maps to es-CR refusal) and surface `DbUpdateConcurrencyException` → es-CR retry message on every mutating method (RowVersion optimistic concurrency, Constitution Quality Gate) in `src/FundingPlatform.Infrastructure/Services/DisbursementService.cs` (depends: T026, T028, T041)
- [ ] T043 [US4] Emit `AdminAuditEvent` (`disbursement.recorded/edited/evidence_attached/evidence_replaced/validated/cancelled`) with `{disbursementId, applicationId, before?, after?}` payload in every mutating service method (depends: T019, T025, T026, T027, T028, T041)
- [ ] T044 [US4] Integration `tests/FundingPlatform.Tests.Integration/DisbursementAuditTests.cs` (real SQL): each action writes one `disbursement.*` row with actor + before/after (depends: T043)

**Checkpoint**: lifecycle + immutability + audit complete.

---

## Phase 7: User Story US5 — Role scoping & read-only visibility (Priority: P5)

**Goal**: Financial Operator acts only within its groups; Auditor/Admin read-only; out-of-group indistinguishable from non-existent; applicant refused.

**Independent Test**: In-group operator acts; out-of-group → flat 404; Auditor/Admin read-only (write POST → 403); applicant refused. (SC-008)

- [ ] T045 [P] [US5] E2E `tests/FundingPlatform.Tests.E2E/Tests/DisbursementRoleScopingTests.cs`: `FinancialOperator_InGroup_CanAct__OutOfGroup_404`, `AuditorAndAdmin_ReadOnly_NoWriteControls__WritePost_403`, `Applicant_Refused`
- [ ] T046 [US5] Add group-overlap scoping to `DisbursementController` (`IReviewerScopeProvider.GetForUserAsync` + `IApplicationRepository.ApplicantSharesAnyGroupAsync`, admin short-circuit, flat `NotFound()` for out-of-group/not-executed) and a read-only write-guard rejecting `Auditor` on POST actions (403) (depends: T029)
- [ ] T047 [US5] Hide write controls for Auditor/Admin-read in `Index.cshtml`/`Detail.cshtml`; confirm `disbursement-inbox` sidebar entry shows for `Financial Operator,Admin` only (depends: T030, T020)

**Checkpoint**: all five stories independently functional.

---

## Phase 8: Polish & Cross-Cutting

- [ ] T048 Run filtered E2E for `DisbursementReconciliation`/`ParticipantBalance`/`DisbursementPartialAndOver`/`DisbursementLifecycle`/`DisbursementRoleScoping` + affected regressions; confirm green (quickstart.md gate)
- [ ] T049 [P] es-CR copy sweep + cleanup: no English literals in views/resources; verify size-guard 413 + magic-byte rejection messages are es-CR
- [ ] T050 On merge: add a Recent Changes entry to `CLAUDE.md`, flip `brainstorm/00-overview.md` + `41-financial-disbursement-platform.md` P1 status to shipped with the PR number (finish-branch convention)

---

## Dependencies & Execution Order

- **Setup (T001–T002)** → **Foundational (T003–T022)** blocks everything. Within Foundational: enums (T003) before entities (T005–T007) before DbSet/EF (T013–T016); evaluator (T008) before its unit test (T009); tables (T010–T012) + configs (T013–T015) + DbSets (T016) before the enum-materialization integration test (T022).
- **US1 (T023–T032)** is the MVP; depends only on Foundational. Service methods are sequential (T025→T026→T027; T028 after T025); controller (T029) after services; views (T030)/resources (T031) [P].
- **US2 (T033–T036)** depends on Foundational + `RecordAsync` (T025); otherwise independent of US1 UI.
- **US3 (T037–T039)** depends on US1 service methods (T025/T028) + US2 balance card (T035).
- **US4 (T040–T044)** depends on US1 controller/services (T029) + audit vocab (T019).
- **US5 (T045–T047)** depends on US1 controller (T029) + role wiring (T020).
- **Polish (T048–T050)** last.

### Parallel opportunities

- Foundational [P]: T003, T004, T006, T007 (after T005 for entity refs), T010–T015, T017, T019 run in parallel across different files.
- Each story's E2E test task ([P]) and its views/resources ([P]) parallelize with sibling tasks.
- After Foundational, US1 and US2's non-UI service work can proceed in parallel by different developers; US3/US4/US5 layer on once US1's controller/services land.

## Implementation Strategy

**MVP = Setup + Foundational + US1.** Stop at the US1 checkpoint and validate the reconciliation thread (AC-001) before proceeding. Then layer US2 (balance) → US3 (partial/over) → US4 (lifecycle/audit) → US5 (scoping), each an independently testable increment. Commit after each task or logical group (Constitution: Commit Discipline).

## Notes

- **Real-SQL enum gotcha (T022):** the `HasConversion<byte>()` TINYINT columns MUST be materialized against SQL Server — EF-InMemory hides the `Byte→Int32` throw (035/040 lesson). Do not rely on InMemory.
- **Two-SaveChanges pattern:** row+ledger in `SaveChanges #1`, audit in `SaveChanges #2` (no user-initiated transaction — the retrying execution strategy forbids it). Mirrors `FundsUsageEvidenceService`.
- **No new managed dependencies; additive dacpac-only schema.**
