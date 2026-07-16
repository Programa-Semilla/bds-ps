---
description: "Task list for Tranches & Budget-Lines (Financial Execution P2)"
---

# Tasks: Tranches & Budget-Lines (Financial Execution P2)

**Input**: Design documents from `specs/046-tranches-budget-lines/`
**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md), [data-model.md](./data-model.md), [contracts/interfaces.md](./contracts/interfaces.md)

**Tests**: INCLUDED — Constitution III (E2E non-negotiable) + integration MUST hit real SQL. Every user story carries unit + integration + filtered-E2E tasks.

**Organization**: grouped by user story (US1 P1, US2 P2, US3 P2, US4 P3). Delivery gate per CLAUDE.md = the **filtered** E2E classes exercising the change (not the full ~30-min suite).

## Format: `[ID] [P?] [Story] Description`

- **[P]**: parallelizable (different files, no dependency on an incomplete task)
- **[Story]**: US1–US4; Setup/Foundational/Polish carry no story label
- All paths are repo-relative.

---

## Phase 1: Setup (shared scaffolding — no schema/behavior yet)

- [ ] T001 [P] Add `ItemCommitState : byte { Uncommitted=0, Committed=1 }` in `src/FundingPlatform.Domain/Enums/ItemCommitState.cs`, and extend `src/FundingPlatform.Domain/Enums/ReconciliationComparison.cs` with `DisbursementSplitVsTotal = 3` and `LinePaymentVsBudget = 4`.
- [ ] T002 [P] Add es-CR `src/FundingPlatform.Web/Resources/TrancheResources.cs` and add the six new reason codes (`SplitMismatch`, `LineNotCommitted`, `LineOverpayment`, `LineHasPayment`, `TrancheFrozen`, `TrancheNameInUse`) to `src/FundingPlatform.Application/Disbursements/DisbursementReasons.cs` + their es-CR strings in `src/FundingPlatform.Web/Resources/DisbursementResources.cs` (per contracts §5).
- [ ] T003 [P] Add `AdminAuditEvent` action constants (`tranche.created/renamed/deleted/item_assigned/item_unassigned`, `line.committed/uncommitted`) + `TargetTypeTranche` in `src/FundingPlatform.Domain/Entities/AdminAuditEvent.cs`, and add `tranche.`→`TargetTypeTranche` (id from payload `trancheId`) and `line.`→`Item` (id from `itemId`) prefix routing in `src/FundingPlatform.Infrastructure/Audit/AdminAuditEventWriter.cs`.

---

## Phase 2: Foundational (BLOCKING — schema + core model all stories build on)

**⚠️ CRITICAL**: no user story work begins until this phase is complete. One dacpac schema landing + the shared domain/EF surface.

- [ ] T004 dacpac: new table `src/FundingPlatform.Database/Tables/dbo.Tranches.sql` (PK clustered Id; `FK_Tranches_Applications` NO ACTION; `Name NVARCHAR(60) NOT NULL`; `Ordinal INT`; `CreatedAtUtc/UpdatedAtUtc DATETIMEOFFSET(0)` + `DF_`; `ROWVERSION`; `IX_Tranches_ApplicationId`; `UX_Tranches_ApplicationId_Name UNIQUE (ApplicationId, Name)`).
- [ ] T005 dacpac: new join table `src/FundingPlatform.Database/Tables/dbo.DisbursementLineAllocations.sql` following the `dbo.ItemImpacts.sql` topology (PK Id; `FK_…_Disbursements` **CASCADE**; `FK_…_Items` **NO ACTION**; `CK_…_Amount_Positive CHECK([Amount]>0)`; `UX_DisbLineAlloc_Disbursement_Item UNIQUE (DisbursementId, ItemId)`; `IX_DisbLineAlloc_ItemId ON (ItemId)`; `ROWVERSION`).
- [ ] T006 dacpac: edit `src/FundingPlatform.Database/Tables/dbo.Items.sql` — add `[TrancheId] INT NULL`, `[CommitState] TINYINT NOT NULL CONSTRAINT [DF_Items_CommitState] DEFAULT (0)`, `FK_Items_Tranches … ON DELETE NO ACTION`, and `IX_Items_TrancheId … WHERE [TrancheId] IS NOT NULL` (inline, no post-deploy backfill — spec 032/037 precedent).
- [ ] T007 [P] Domain entity `src/FundingPlatform.Domain/Entities/Tranche.cs` (sealed; fields per data-model; `static Create(applicationId, name, ordinal)` + `Rename(name)` with trim/≤60/non-empty guards; `RowVersion`).
- [ ] T008 [P] Domain entity `src/FundingPlatform.Domain/Entities/DisbursementLineAllocation.cs` (sealed; `static For(disbursementId, itemId, amount)` amount>0; no mutators).
- [ ] T009 Edit `src/FundingPlatform.Domain/Entities/Item.cs` — add `TrancheId`/`CommitState` properties + `internal AssignTranche(int?)`, `internal Commit()` (guard idempotent), `internal Uncommit()` (entity-level; payment guard lives in service).
- [ ] T010 Edit `src/FundingPlatform.Domain/Entities/Application.cs` — add `_tranches` list + `Tranches` accessor + aggregate methods `CreateTranche`/`RenameTranche`/`DeleteTranche`/`AssignItemToTranche` (mirror `AssignLineCodeToItem`: sibling-uniqueness, delegate), each guarding `State != AgreementExecuted` (freeze, D4); `DeleteTranche` re-parents member items to `TrancheId=null`.
- [ ] T011 [P] Edit `src/FundingPlatform.Domain/ValueObjects/ParticipantBalance.cs` — add `Committed` (5→6 dims, `Available = Allocated − Paid` unchanged); add `LineOverpaymentDiscrepancy` + `LinePaymentVsBudget` VOs.
- [ ] T012 [P] Extract `static decimal LineBudget(Item item)` in `src/FundingPlatform.Application/Services/ApplicationCurrencyTotal.cs` (selected-quote `ConvertedCrcAmount`, skip legacy-needs-review) and refactor `Compute` to call it (behavior-preserving).
- [ ] T013 EF config: new `TrancheConfiguration.cs` + `DisbursementLineAllocationConfiguration.cs`; edit `ItemConfiguration.cs` (`CommitState` `HasConversion<byte>()`, `TrancheId` FK `Restrict`, filtered `IX_Items_TrancheId`); map `Application._tranches` backing field; add `DbSet<Tranche>` + `DbSet<DisbursementLineAllocation>` to `src/FundingPlatform.Infrastructure/Persistence/AppDbContext.cs` (all under Configurations/).
- [ ] T014 [P] Composed-balance DTOs (`ComposedBalance`/`TrancheBalance`/`BudgetLineBalance`/`BudgetLineStatus`) + `GetComposedForApplicationAsync` signature on `IParticipantBalanceProjection` in `src/FundingPlatform.Application/Disbursements/` (per data-model / contracts §3).

**Checkpoint**: schema deploys clean on real SQL; solution builds; model ready.

---

## Phase 3: User Story 1 — Reviewer subdivides the allocation into tranches (Priority: P1) 🎯 MVP

**Goal**: reviewer groups line items into tranches on the pre-audit surface; derived amounts; synthetic default tranche; frozen at execution.
**Independent Test**: create 2 tranches, assign lines, Σ tranche = allocation to the colón; unassigned line → synthetic "General"; tranche edits refused after execution.

### Tests for US1

- [ ] T015 [P] [US1] Unit tests in `tests/FundingPlatform.Tests.Unit/` — `Application` tranche methods (create/rename/delete/assign, duplicate-name reject, `AgreementExecuted` freeze throw, delete re-parents to synthetic) + `Tranche` guard invariants.
- [ ] T016 [P] [US1] Integration tests (real SQL) in `tests/FundingPlatform.Tests.Integration/` — `TrancheService` CRUD + assignment; `UX_Tranches_ApplicationId_Name` duplicate race → `TrancheNameInUse`; derived Σ tranche amount == `DisbursementAllocation.ResolveAsync` snapshot (SC-001); synthetic tranche present iff a line has null `TrancheId`.
- [ ] T017 [P] [US1] E2E `TrancheAdminTests` in `tests/FundingPlatform.Tests.E2E/` — reviewer defines tranches on `Review/{id}`, assigns lines, sees Σ=allocation + synthetic "General"; after execution tranche edits are refused (frozen).

### Implementation for US1

- [ ] T018 [US1] `ITrancheService` + `TrancheDtos` in `src/FundingPlatform.Application/Tranches/` (per contracts §1).
- [ ] T019 [US1] `TrancheService` in `src/FundingPlatform.Infrastructure/Services/TrancheService.cs` — CRUD+assign via `Application` aggregate, two-SaveChanges `tranche.*` audit, accent/case dup pre-check (`CompanyNameNormalizer`) + index backstop, `TrancheFrozen` on post-execution edit; register in `DependencyInjection.cs`.
- [ ] T020 [US1] `TrancheController` in `src/FundingPlatform.Web/Controllers/TrancheController.cs` — `[Authorize(Roles="Reviewer,Admin")]`, `[Route("Review/{applicationId:int}/Tranches")]`, group-overlap `Forbid`, antiforgery POSTs (Create/Rename/Delete/Assign).
- [ ] T021 [US1] `_TrancheEditor.cshtml` + `TrancheEditorViewModel` in `src/FundingPlatform.Web/` — assign items→tranches (`data-searchable`, spec 031); render on `Views/Review/Review.cshtml` when `ShowReviewerChecklist == true`; es-CR copy in `TrancheResources`.
- [ ] T022 [US1] Composed projection (partial) in `src/FundingPlatform.Infrastructure/Services/ParticipantBalanceProjection.cs` — `GetComposedForApplicationAsync` returning tranche/line tree with **Allocated** per line (`LineBudget`) → tranche → participant, synthetic "General" for null `TrancheId`; wire DI.

**Checkpoint**: US1 independently testable — tranche structure + Allocated composition work end to end.

---

## Phase 4: User Story 2 — Financial Operator commits budget-lines (Priority: P2)

**Goal**: obligate a line before paying; Committed dimension; reversible until first payment.
**Independent Test**: commit two lines → Committed rises by their budgets at all levels; un-commit one; un-commit a paid line → refused.

### Tests for US2

- [ ] T023 [P] [US2] Unit tests in `tests/FundingPlatform.Tests.Unit/` — `Item.Commit`/`Uncommit` invariants; Committed = Σ committed line budgets.
- [ ] T024 [P] [US2] Integration tests (real SQL) — `CommitLineAsync`/`UncommitLineAsync`; un-commit refused with an attributed payment → `LineHasPayment` (FR-007); Committed dimension populated in the projection; `CommitState` `HasConversion<byte>()` materializes on real SQL.
- [ ] T025 [P] [US2] E2E `BudgetLineCommitTests` — Financial Operator commits/un-commits; Committed rises/falls at line/tranche/participant; Auditor/Admin see read-only (FR-021); un-commit-with-payment refused.

### Implementation for US2

- [ ] T026 [US2] `IDisbursementService.CommitLineAsync`/`UncommitLineAsync` + impl in `src/FundingPlatform.Infrastructure/Services/DisbursementService.cs` (`Item.Commit`/`Uncommit`; un-commit guard queries `DisbursementLineAllocation` for non-cancelled payments; `line.committed`/`line.uncommitted` audit).
- [ ] T027 [US2] `DisbursementController` Commit/Uncommit routes (`POST "Lines/{itemId:int}/Commit"` / `/Uncommit`) in `src/FundingPlatform.Web/Controllers/DisbursementController.cs` — `GuardWriteAsync` (executed+group 404 → role 403).
- [ ] T028 [US2] Projection: add **Committed** to line/tranche/participant balances in `ParticipantBalanceProjection.cs`; update flat `GetForApplicationAsync` to 6-dim; update `Views/Disbursement/_BalanceCard.cshtml`.
- [ ] T029 [US2] `_BudgetLineRow.cshtml` (commit/un-commit buttons + `CommitState`) + `_TrancheBalancePanel.cshtml`; render on `Views/Disbursement/Index.cshtml` from the composed model.

**Checkpoint**: US1 + US2 both work independently; Committed dimension live.

---

## Phase 5: User Story 3 — Financial Operator attributes disbursements to lines (Priority: P2)

**Goal**: split a disbursement across committed lines (M:N, may span tranches); split-integrity + per-line over-payment blocking; Paid/Validated/Pending compose per line.
**Independent Test**: split a disbursement across two lines in two tranches; mismatched split rejected; over-paying a line blocks Validar; over-payment shows negative Available.

**Depends on US2** (attribution only to committed lines — obligate-then-pay).

### Tests for US3

- [ ] T030 [P] [US3] Unit tests in `tests/FundingPlatform.Tests.Unit/` — `DisbursementLineReconciliation.EvaluateSplit` (mismatch → blocking) + `EvaluateLineOverpayments` (Paid−Committed ≥ 0.01 → blocking).
- [ ] T031 [P] [US3] Integration tests (real SQL) — `RecordAsync`/`EditAsync` persist/replace splits; `SplitMismatch` + `LineNotCommitted` rejects; `ValidateAsync` blocks `LineOverpayment` re-checked against **fresh** sums (concurrent-attribution race); per-line Paid/Validated/Pending compose (SC-002/SC-003/SC-004).
- [ ] T032 [P] [US3] E2E `LineAttributionTests` — record a disbursement split across lines spanning tranches; mismatch rejected; over-payment blocks Validar; negative Available visible, never clamped.

### Implementation for US3

- [ ] T033 [US3] Pure `src/FundingPlatform.Domain/Services/DisbursementLineReconciliation.cs` (`EvaluateSplit` + `EvaluateLineOverpayments`, `MinDetectableDifference = 0.01`, all Blocking).
- [ ] T034 [US3] `RecordDisbursementCommand`/`EditDisbursementCommand` gain `IReadOnlyList<LineAllocationInput> Lines`; `DisbursementService.RecordAsync`/`EditAsync` validate committed+split (blocking reasons) and persist the `DisbursementLineAllocation` set replace-all; audit payload gains `lines`.
- [ ] T035 [US3] `DisbursementService.ValidateAsync` per-line over-payment gate after P1's evidence/participant checks — fresh per-line committed budgets + non-cancelled sums → `EvaluateLineOverpayments` → block `LineOverpayment` (no ledger post).
- [ ] T036 [US3] Projection: per-line **Paid/Validated/Pending/Available** from `DisbursementLineAllocation` in `ParticipantBalanceProjection.cs` (completes the composed tree; participant `Allocated` cross-checks the ledger snapshot).
- [ ] T037 [US3] Record/Edit split-editor UI in `Views/Disbursement/Index.cshtml`/`Detail.cshtml` + `DisbursementViewModels.cs` binding (per-line amount inputs summing to amount) + es-CR copy.

**Checkpoint**: US1–US3 work; full six-dimension composed balances + reconciliation live.

---

## Phase 6: User Story 4 — Viewing and filtering budget-lines (Priority: P3)

**Goal**: view execution by tranche/line and filter budget-lines.
**Independent Test**: filter by tranche/status/supplier/validation-state → list narrows.

**Depends on US1–US3** (status derives from commit + attribution).

### Tests for US4

- [ ] T038 [P] [US4] Integration tests (real SQL) — filter budget-lines by tranche, status (`Uncommitted/Committed/PartiallyPaid/Paid/Validated`, D3), supplier, validation state, and date (FR-020; participant is inherent on the per-application surface).
- [ ] T039 [P] [US4] E2E `BudgetLineFilterTests` — apply each filter, list narrows correctly (SC-005).

### Implementation for US4

- [ ] T040 [US4] `BudgetLineStatus` derivation (D3) in `ParticipantBalanceProjection.cs` (computed in-query, not stored).
- [ ] T041 [US4] Filter parameters on the composed projection (tranche, status, supplier, validation state, date — FR-020) + filter toolbar on `Views/Disbursement/Index.cshtml` (`data-searchable`, es-CR labels in `TrancheResources`).

**Checkpoint**: all four stories independently functional.

---

## Phase 7: Polish & Cross-Cutting

- [ ] T042 [P] P1 regression + SC-006: integration + E2E — a pre-P2 executed application (no tranche rows, `CommitState` default 0) yields P1 balances unchanged and one synthetic tranche; existing `Disbursement*` E2E green.
- [ ] T043 [P] Composed-projection performance sanity: no N+1 on an application with many lines/tranches (bounded correlated queries, `AsNoTracking`).
- [ ] T044 Run `quickstart.md` walkthrough end to end; execute the **filtered** E2E set (`Tranche*`, `BudgetLine*`, `LineAttribution*` + `Disbursement*` regression) and confirm green (delivery gate).
- [ ] T045 On completion: update CLAUDE.md Recent Changes + `brainstorm/41-financial-disbursement-platform.md` (P2 shipped) — deferred to ship/PR.

---

## Dependencies & Execution Order

### Phase dependencies
- **Setup (Phase 1)**: start immediately; T001–T003 all [P].
- **Foundational (Phase 2)**: after Setup; **blocks all stories**. T004→T006 ordered (Tranches table before Items FK); T007/T008/T011/T012/T014 [P]; T009 after T007; T010 after T007; T013 after T007–T010.
- **US1 (Phase 3)**: after Foundational. MVP.
- **US2 (Phase 4)**: after Foundational (independent of US1 via synthetic tranche).
- **US3 (Phase 5)**: after **US2** (obligate-then-pay).
- **US4 (Phase 6)**: after US1–US3.
- **Polish (Phase 7)**: after all desired stories.

### Within a story
- Tests written first and failing before implementation (RED→GREEN, Constitution III).
- Domain/service before controller before view.

### Parallel opportunities
- Setup: T001/T002/T003 together.
- Foundational: T007, T008, T011, T012, T014 together (then T009/T010/T013).
- Each story's test tasks ([P]) together; different stories parallelizable across developers once Foundational lands (US3 waits on US2).

---

## Parallel Example: Foundational

```bash
# After schema (T004–T006), launch the independent model tasks together:
Task: "Domain entity Tranche.cs (T007)"
Task: "Domain entity DisbursementLineAllocation.cs (T008)"
Task: "Extend ParticipantBalance with Committed + line VOs (T011)"
Task: "Extract ApplicationCurrencyTotal.LineBudget (T012)"
Task: "Composed-balance DTOs + projection signature (T014)"
```

## Implementation Strategy

### MVP (US1 only)
1. Phase 1 Setup → 2. Phase 2 Foundational → 3. Phase 3 US1 → **STOP & validate** (tranche structure + Allocated composition) → demo.

### Incremental delivery
US1 (structure) → US2 (Committed) → US3 (attribution + reconciliation) → US4 (filtering). Each adds value without breaking the prior; commit after each task or logical group; filtered E2E per story is the checkpoint gate.
