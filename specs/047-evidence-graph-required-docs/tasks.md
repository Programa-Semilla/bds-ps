# Tasks: Evidence Graph & Required-Document Rules (spec 047)

**Input**: Design documents from `specs/047-evidence-graph-required-docs/` (plan.md, spec.md, research.md, data-model.md, contracts/interfaces.md, quickstart.md)
**Program**: Financial-execution slice **P3 of 9** (builds on P1/045, P2/046).

**Tests**: E2E is the Constitution-mandated delivery bar (Principle III) — test tasks are **included** per story. Unit/Integration complement (Integration hits real SQL, never mocks).

**Format**: `- [ ] [ID] [P?] [Story?] Description with file path` · `[P]` = parallelizable (different files, no incomplete deps).

**Conventions**: every enum column is `TINYINT` + EF `HasConversion<byte>()`; dacpac is the schema source (no EF migrations); `Items` column adds are nullable-safe inline (no backfill); service-produced reasons live in `Application/*Reasons.cs`, view copy in `Web/Resources/*Resources.cs`; audit uses the two-SaveChanges pattern; controllers reuse P1/P2 group-scope (`IsAccessibleAsync` flat-404) + `GuardWriteAsync` (`CanWrite() => IsInRole("Financial Operator")`).

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: shared enums, storage, and audit scaffolding used by every story.

- [X] T001 [P] Add `EvidenceType : byte` enum (BankReceipt=0, Invoice=1, SignedAcceptance=2, CreditNote=3, RefundReceipt=4, Other=5) in `src/FundingPlatform.Domain/Enums/EvidenceType.cs`
- [X] T002 [P] Add `ItemClosureState : byte` enum (Open=0, Closed=1) in `src/FundingPlatform.Domain/Enums/ItemClosureState.cs`
- [X] T003 [P] Add `FileCategory.Evidence` (container `evidence`, 20 MiB, `ServingMode.BackendStream`) in `src/FundingPlatform.Application/Abstractions/Storage/FileCategory.cs` + the matching entry in `StorageOptions.cs`
- [X] T004 [P] Add es-CR view copy `src/FundingPlatform.Web/Resources/EvidenceResources.cs` (evidence-type + closure/status label + badge switch helpers, mirroring `TrancheResources`)
- [X] T005 [P] Add es-CR view copy `src/FundingPlatform.Web/Resources/DocRuleResources.cs` (matrix labels/buttons/titles)

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: audit vocabulary + reason files that all writer stories depend on. **Blocks Phase 3+.**

- [X] T006 Add `docrule.*` / `evidence.*` / `closure.*` action-key constants + `TargetTypeDocRule/Evidence/Closure` discriminators in `src/FundingPlatform.Domain/Entities/AdminAuditEvent.cs` (new spec-047 banner after the spec-046 `line.*` block)
- [X] T007 Add `docrule.` / `evidence.` / `closure.` `StartsWith` branches (parse `categoryId`/`evidenceId`/`itemId` via existing `ExtractIntId`) in `src/FundingPlatform.Infrastructure/Audit/AdminAuditEventWriter.cs::DeriveTarget`
- [X] T008 [P] Create `src/FundingPlatform.Application/Evidence/EvidenceReasons.cs` with nested `Codes` (Orphaned, AllocationExceedsAmount, ReasonRequired, EvidenceLocked, MissingRequiredDocuments, PaymentNotValidated, LineEqualityMismatch, RequiredEvidenceNotFullyAllocated, AlreadyClosed, NotClosed) + es-CR messages (mirror `DisbursementReasons`)
- [X] T009 [P] Create `src/FundingPlatform.Application/DocRules/DocRuleReasons.cs` with nested `Codes` + es-CR messages

**Checkpoint**: audit + reasons ready — story work can begin.

---

## Phase 3: User Story 1 — Typed evidence graph with per-line allocation (Priority: P1) 🎯 MVP

**Goal**: attach typed evidence with metadata, link M:N to budget-lines with per-line allocation, download; allocation ≤ amount; no orphans.
**Independent Test**: `EvidenceGraphAllocationTests` — one invoice across lines 1–4 (AC-002), five invoices on one line (AC-003), over-allocation refused, orphan refused, payment-independent acceptance stored.

- [X] T010 [P] [US1] `Evidence` aggregate (Application-scoped; `Attach` factory, `ReplaceCurrent`, owns `_versions`, private setters, `RowVersion`) in `src/FundingPlatform.Domain/Entities/Evidence.cs`
- [X] T011 [P] [US1] `EvidenceVersion` immutable child (VersionNumber, IsCurrent, file+critical-field snapshot, FileHash, Reason, actor/time; supersede transition) in `src/FundingPlatform.Domain/Entities/EvidenceVersion.cs`
- [X] T012 [P] [US1] `EvidenceLineAllocation` M:N join (`For(evidenceId,itemId,amount)` factory, amount>0, no mutators, `RowVersion`) in `src/FundingPlatform.Domain/Entities/EvidenceLineAllocation.cs`
- [X] T013 [US1] EF configs `EvidenceConfiguration.cs`, `EvidenceVersionConfiguration.cs`, `EvidenceLineAllocationConfiguration.cs` in `src/FundingPlatform.Infrastructure/Persistence/Configurations/` (decimal(18,2); `HasConversion<byte>()` on Type; `EvidenceVersion` one-current filtered unique; alloc Evidence=Cascade / Item=ClientCascade + UX_(EvidenceId,ItemId) + IX_ItemId; FKs to AspNetUsers/Suppliers NO ACTION)
- [X] T014 [US1] Dacpac tables `dbo.Evidence.sql`, `dbo.EvidenceVersions.sql`, `dbo.EvidenceLineAllocations.sql` in `src/FundingPlatform.Database/Tables/` (CHECK Amount>0; `UX_EvidenceVersions_OneCurrent ... WHERE [IsCurrent]=1`; `UX_EvidenceLineAlloc_Evidence_Item`; Evidence FK→Applications NO ACTION, Version FK→Evidence CASCADE, Alloc FK→Evidence CASCADE / Items NO ACTION)
- [X] T015 [US1] Register `Evidence`, `EvidenceVersions`, `EvidenceLineAllocations` DbSets in `src/FundingPlatform.Infrastructure/Persistence/AppDbContext.cs`
- [X] T016 [US1] `IEvidenceService` + commands/DTOs (`AttachEvidenceCommand`, `AllocateEvidenceCommand`, `EvidenceSummary`, `EvidenceDetail`, `EvidenceVersionRow`, `EvidenceDownload`, `LineAllocationInput`) in `src/FundingPlatform.Application/Evidence/IEvidenceService.cs`
- [X] T017 [US1] `EvidenceService` impl (Attach: blob upload → v1 → orphan guard → alloc integrity → two-SaveChanges `evidence.attached` audit + best-effort blob cleanup on failure; Allocate: replace-all rows mirroring `ReplaceSplitAsync` + `evidence.allocated` audit; List/Get/OpenForDownload; Delete pre-close) in `src/FundingPlatform.Infrastructure/Services/EvidenceService.cs`
- [X] T018 [US1] Register `IEvidenceService → EvidenceService` (`AddScoped`) in `src/FundingPlatform.Infrastructure/DependencyInjection.cs`
- [X] T019 [US1] `EvidenceController` (`[Authorize(Roles="Financial Operator,Admin,Auditor")]`, route `Applications/{applicationId:int}/Evidence`; `IsAccessibleAsync` flat-404 + `GuardWriteAsync`; `[UploadSizeGuard]` + magic-byte `EvidenceFileTypePolicy`; Index/Detail/Attach/Allocate/Delete/Download) in `src/FundingPlatform.Web/Controllers/EvidenceController.cs`
- [X] T020 [P] [US1] `EvidenceViewModels.cs` in `src/FundingPlatform.Web/ViewModels/`
- [X] T021 [US1] Views `Views/Evidence/Index.cshtml`, `Detail.cshtml`, `_EvidenceRow.cshtml`, `_AllocationEditor.cshtml` (attach form + per-line allocation inputs; `data-testid` hooks) in `src/FundingPlatform.Web/Views/Evidence/`
- [X] T022 [P] [US1] Unit `EvidenceAllocationTests` (allocation-integrity: Σ ≤ amount; orphan guard) in `tests/FundingPlatform.Tests.Unit/`
- [X] T023 [P] [US1] Integration `EvidenceGraphTests` (real SQL: attach, M:N both directions, over-allocation refused, cascade delete of versions/allocations) in `tests/FundingPlatform.Tests.Integration/`
- [ ] T024 [US1] E2E `EvidenceGraphAllocationTests` (AC-002 one-invoice-4-lines, AC-003 five-invoices-one-line, over-allocation refused, orphan refused, acceptance-without-payment; **+ SC-007 cross-cutting**: out-of-group Financial Operator → flat 404 on every `/Evidence` route, Auditor/Admin read-only → 403 on write, >20 MiB / bad magic bytes rejected) in `tests/FundingPlatform.Tests.E2E/`

**Checkpoint US1**: evidence graph + allocation delivered and independently testable (AC-002/AC-003). Commit + push.

---

## Phase 4: User Story 2 — Required-document rules & live completeness (Priority: P2)

**Goal**: admin configures per-Category (+ global default) required doc types; per-line live completeness (present vs missing), reading BOTH disbursement-anchored and graph evidence.
**Independent Test**: `RequiredDocMatrixCompletenessTests` — mark Invoice+Acceptance required; a line with only a receipt shows both missing (AC-005); disbursement invoice counts as present (D1); category falls back to global default.
**Depends on**: US1 (evidence to check).

- [X] T025 [P] [US2] `DocumentRuleSet` aggregate (CategoryId nullable, owns `_items`, full-replace, `RowVersion`) in `src/FundingPlatform.Domain/Entities/DocumentRuleSet.cs`
- [X] T026 [P] [US2] `DocumentRuleItem` child (EvidenceType, IsRequired) in `src/FundingPlatform.Domain/Entities/DocumentRuleItem.cs`
- [X] T027 [US2] `Item.MissingRequiredDocuments(requiredTypes, presentTypes)` pure helper (mirror `MissingRequiredCategoryFields`) in `src/FundingPlatform.Domain/Entities/Item.cs`
- [X] T028 [US2] EF configs `DocumentRuleSetConfiguration.cs` + `DocumentRuleItemConfiguration.cs` (`HasConversion<byte>()` on EvidenceType; `UNIQUE (CategoryId)`; `UNIQUE (DocumentRuleSetId, EvidenceType)`; item FK CASCADE, Category FK NO ACTION) in `src/FundingPlatform.Infrastructure/Persistence/Configurations/`
- [X] T029 [US2] Dacpac tables `dbo.DocumentRuleSets.sql` + `dbo.DocumentRuleItems.sql` in `src/FundingPlatform.Database/Tables/`
- [X] T030 [US2] Post-deploy seed `src/FundingPlatform.Database/PostDeployment/NN_SeedDocumentRules.sql` (`NN` = next number in the existing `PostDeployment/` sequence) — global-default set (BankReceipt+Invoice+SignedAcceptance = Required); idempotent (`IF NOT EXISTS`)
- [X] T031 [US2] Register `DocumentRuleSets`, `DocumentRuleItems` DbSets in `AppDbContext.cs`
- [X] T032 [US2] `IDocumentRuleService` + DTOs (`ListAsync`, `GetAsync`, `UpsertAsync`, `ResolveRequiredTypes`) in `src/FundingPlatform.Application/DocRules/IDocumentRuleService.cs`
- [X] T033 [US2] `DocumentRuleService` impl (one-set-per-Category enforcement, full-replace items, `docrule.upserted` two-SaveChanges audit) in `src/FundingPlatform.Infrastructure/Services/DocumentRuleService.cs`
- [X] T034 [US2] Completeness resolver: extend `ParticipantBalanceProjection` (or a sibling projection) to compute, per line, required types (via `IDocumentRuleService.ResolveRequiredTypes`) vs present types — **union of** (a) `DisbursementEvidence.Kind` from the line's validated disbursements and (b) graph `Evidence.Type` linked to the line; add `EvidenceIncomplete` + missing-type list to `BudgetLineBalance` DTO in `src/FundingPlatform.Application/Disbursements/ComposedBalanceDtos.cs` + `src/FundingPlatform.Infrastructure/Services/ParticipantBalanceProjection.cs`
- [X] T035 [US2] Register `IDocumentRuleService → DocumentRuleService` in `DependencyInjection.cs`
- [X] T036 [US2] `AdminController` actions `DocumentRules` (list) / `CreateDocumentRule` / `EditDocumentRule` (`[Authorize(Roles="Admin")]`, antiforgery, `TempData` es-CR) in `src/FundingPlatform.Web/Controllers/AdminController.cs`
- [X] T037 [P] [US2] `Admin/DocumentRuleAdminViewModels.cs` in `src/FundingPlatform.Web/ViewModels/Admin/`
- [X] T038 [US2] Views `Views/Admin/DocumentRules.cshtml`, `CreateDocumentRule.cshtml`, `EditDocumentRule.cshtml`, `_DocumentRuleItemsEditor.cshtml` (six-type checkbox matrix) in `src/FundingPlatform.Web/Views/Admin/` + sidebar entry
- [X] T039 [US2] `_CompletenessMatrix.cshtml` partial + wire into `Views/Evidence/Index.cshtml` and the `Disbursement` `Index` line rows (present/missing per required type, `EvidenceIncomplete` badge)
- [X] T040 [P] [US2] Unit `DocumentRuleResolutionTests` (category → set → global fallback → empty; `MissingRequiredDocuments`) in `tests/FundingPlatform.Tests.Unit/`
- [X] T041 [P] [US2] Integration `DocumentRuleMatrixTests` (real SQL: one-per-category, full-replace, both-source completeness incl. disbursement invoice) in `tests/FundingPlatform.Tests.Integration/`
- [ ] T042 [US2] E2E `RequiredDocMatrixCompletenessTests` (admin marks required; line shows missing AC-005; disbursement invoice counts present; global-default fallback) in `tests/FundingPlatform.Tests.E2E/`

**Checkpoint US2**: configurable required docs + live completeness delivered. Commit + push.

---

## Phase 5: User Story 3 — Budget-line closure with reconciliation gate (Priority: P3)

**Goal**: Financial Operator closes a line only when required docs present + payments Validated + `LinePaid == LineAccepted` (colón) + required evidence fully allocated; closed line locks evidence; audited reopen-with-reason; off-ledger.
**Independent Test**: `BudgetLineClosureTests` — happy-path close; ₡72 acceptance shortfall blocks; missing required doc blocks; unvalidated payment blocks; closed line rejects evidence writes; reopen unlocks with no balance change.
**Depends on**: US1 + US2.

- [X] T043 [US3] Add `ClosureState` + closure metadata (`ClosedByUserId`, `ClosedAtUtc`, `ClosureReason`, `ReopenReason`) fields + `internal Close(userId,reason?)` / `Reopen(userId,reason)` idempotent mutators (stamp/clear, mirror `Commit`/`Validate`) in `src/FundingPlatform.Domain/Entities/Item.cs`
- [X] T044 [US3] `ItemConfiguration.cs` — `ClosureState` `.HasConversion<byte>().IsRequired().HasDefaultValue(Open)` + closure metadata mappings + `ClosedByUserId` FK→AspNetUsers NO ACTION in `src/FundingPlatform.Infrastructure/Persistence/Configurations/ItemConfiguration.cs`
- [X] T045 [US3] `dbo.Items.sql` — add `ClosureState TINYINT NOT NULL DEFAULT(0)`, `ClosedByUserId`, `ClosedAtUtc`, `ClosureReason`, `ReopenReason` (nullable-safe inline; FK to AspNetUsers NO ACTION) in `src/FundingPlatform.Database/Tables/dbo.Items.sql`
- [X] T046 [P] [US3] Add pure `EvaluateLineEquality(IReadOnlyList<LineEqualityInput>)` (0.01 tolerance, `Blocking` discrepancies; LinePaid vs LineAccepted) in `src/FundingPlatform.Domain/Services/DisbursementLineReconciliation.cs`
- [X] T047 [US3] `IBudgetLineClosureService` + DTOs (`CloseAsync`, `ReopenAsync`, `GetCompletenessAsync`, `LineCompleteness`) in `src/FundingPlatform.Application/Evidence/IBudgetLineClosureService.cs`
- [X] T048 [US3] `BudgetLineClosureService` impl — close gate re-checks **fresh** reads (completeness both-source, all attributed payments Validated, `EvaluateLineEquality`, required-evidence fully-allocated) → `Item.Close` + `closure.line_closed` audit; `Reopen` → `Item.Reopen` + `closure.line_reopened`; off-ledger (no ledger/balance write) in `src/FundingPlatform.Infrastructure/Services/BudgetLineClosureService.cs`
- [X] T049 [US3] Enforce evidence lock on closed lines: `EvidenceService` Attach/Replace/Allocate/Delete refuse (`EvidenceLocked`) when any target line is Closed in `src/FundingPlatform.Infrastructure/Services/EvidenceService.cs`
- [X] T050 [US3] Extend `DeriveStatus` with leading `if (closed) return Closed;` + add `Closed` to `BudgetLineStatus` in `src/FundingPlatform.Application/Disbursements/ComposedBalanceDtos.cs` + `src/FundingPlatform.Infrastructure/Services/ParticipantBalanceProjection.cs`
- [X] T051 [US3] Register `IBudgetLineClosureService` in `DependencyInjection.cs`
- [X] T052 [US3] `DisbursementController` (or `EvidenceController`) `POST Lines/{itemId}/Close` + `POST Lines/{itemId}/Reopen` (reason required; reuse `IsAccessibleAsync`/`GuardWriteAsync`) + Closed/EvidenceIncomplete status badges in the line rows in `src/FundingPlatform.Web/Controllers/`
- [X] T053 [P] [US3] Unit `LineEqualityReconciliationTests` + `ItemClosureTests` (Close/Reopen idempotency, stamp/clear) in `tests/FundingPlatform.Tests.Unit/`
- [X] T054 [P] [US3] Integration `ClosureGateTests` (real SQL: each block reason; reopen clears + no balance change) in `tests/FundingPlatform.Tests.Integration/`
- [ ] T055 [US3] E2E `BudgetLineClosureTests` (happy close; ₡72 mismatch block; missing-doc block; unvalidated-payment block; evidence-locked-when-closed; reopen unlocks, balances identical) in `tests/FundingPlatform.Tests.E2E/`

**Checkpoint US3**: closure gate delivered. **Run P1/P2 `Disbursement*` regression — must be green (SC-006).** Commit + push.

---

## Phase 6: User Story 4 — Evidence version history (Priority: P4)

**Goal**: replacing a file or a reconciliation-critical field appends a version (reason required), retains prior as superseded, records actor/time/hash; all versions viewable/downloadable.
**Independent Test**: `EvidenceVersionHistoryTests` — replace with reason → both versions viewable (original superseded, actor/reason/hash shown); download v1 & v2; amount edit appends a version; replace without reason refused.
**Depends on**: US1 (`EvidenceVersion` table already created in T011/T014).

- [X] T056 [US4] `EvidenceService.ReplaceAsync` — new-version append (mirror `FundingAgreement.ReplacePendingUpload`: supersede current, add current), require `Reason`, recompute SHA-256 `FileHash`, `evidence.replaced` audit; trigger on file replace OR reconciliation-critical field edit in `src/FundingPlatform.Infrastructure/Services/EvidenceService.cs`
- [X] T057 [US4] `IEvidenceService.ReplaceAsync` + `GetVersionsAsync` + version-aware `OpenForDownloadAsync(?versionNumber)` in `src/FundingPlatform.Application/Evidence/IEvidenceService.cs`
- [X] T058 [US4] Web: `EvidenceController` `POST {id}/Replace` + `GET {id}/Download?v=` + `_VersionHistory.cshtml` partial on `Detail.cshtml` (version list with actor/timestamp/reason/hash + per-version download) in `src/FundingPlatform.Web/`
- [ ] T059 [P] [US4] Unit `EvidenceVersionTests` (append/supersede, one-current invariant, reason-required) in `tests/FundingPlatform.Tests.Unit/`
- [ ] T060 [US4] E2E `EvidenceVersionHistoryTests` (replace+reason → both viewable; download v1/v2; amount-edit versions; no-reason refused) in `tests/FundingPlatform.Tests.E2E/`

**Checkpoint US4**: version history delivered. Commit + push.

---

## Phase 7: Polish & Cross-Cutting

- [ ] T061 [P] Batch the per-line completeness resolve on the disbursement/evidence `Index` (avoid N+1 across lines) in `ParticipantBalanceProjection`
- [ ] T062 [P] es-CR copy sweep (no English-only strings) across new views/resources; confirm sidebar `DocumentRules` admin entry + evidence link on the disbursement panel
- [ ] T063 Run the filtered E2E suite (`EvidenceGraphAllocation|RequiredDocMatrixCompleteness|BudgetLineClosure|EvidenceVersionHistory`) + P1/P2 `Disbursement*` regression; capture green results (delivery bar)
- [ ] T064 Deep review (`/speckit-spex-deep-review` or the plan-review gate follow-through); apply Critical/Important fixes; write `specs/047-evidence-graph-required-docs/review-findings.md`
- [ ] T065 Post-merge: flip CLAUDE.md active-plan → shipped + PR#, add a Recent Changes entry, and update `brainstorm/41-financial-disbursement-platform.md` (P3 → shipped)

---

## Dependencies & Execution Order

- **Setup (P1) → Foundational (P2)** block everything.
- **US1 (P3)** is the MVP foundation; **US2, US3, US4 all depend on US1**. **US3 depends on US2** (completeness feeds the close gate). **US4 depends only on US1** (can run in parallel with US2/US3 after US1).
- Recommended order: **US1 → US2 → US3 → US4** (spec priority), or **US1 → (US2 ∥ US4) → US3**.
- Shared-file tasks are **not** `[P]` with each other: `AppDbContext.cs` (T015/T031), `DependencyInjection.cs` (T018/T035/T051), `AdminAuditEvent.cs`/`Writer` (T006/T007), `Item.cs` (T027/T043), `ComposedBalanceDtos.cs`/`ParticipantBalanceProjection.cs` (T034/T050/T061).

## Parallel Opportunities (examples)

- **Setup**: T001–T005 all `[P]`.
- **US1 entities**: T010, T011, T012 `[P]` (then T013 config, T014 dacpac serialize on shared knowledge).
- **US1 tests**: T022, T023 `[P]`.
- **US2**: T025, T026 `[P]`; tests T040, T041 `[P]`.

## Implementation Strategy

- **MVP = US1** (evidence graph + allocation) — already delivers AC-002/AC-003.
- Deliver incrementally, committing at each story checkpoint; keep the P1/P2 `Disbursement*` E2E regression green throughout (SC-006).
- Total: **65 tasks** — US1: 15, US2: 18, US3: 13, US4: 5, Setup/Foundational: 9, Polish: 5.
