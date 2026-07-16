# Implementation Plan: Tranches & Budget-Lines (Financial Execution P2)

**Branch**: `046-tranches-budget-lines` | **Date**: 2026-07-16 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `specs/046-tranches-budget-lines/spec.md`

## Summary

Extend the spec-045 (P1) money-execution spine with structure. Today a `Disbursement` is keyed by `ApplicationId` and carries a single `Amount`; balances are a flat 5-dimension `ParticipantBalance` computed from the append-only ledger. P2 adds:

1. **`Tranche`** — a new per-application entity (named funding phase) that groups the application's line items (`Item`s). A tranche's amount is **derived** (Σ its lines' budgets), so Σ tranche = allocation is structural. Assigned by the reviewer on the pre-audit review surface; frozen at `AgreementExecuted`. Unassigned lines fall into a **virtual default tranche** (no row) — this covers FR-002 and every pre-P2 executed application with zero data migration.
2. **Per-line commit** — a `CommitState` TINYINT on `Item` (off-ledger operational status, resolving research OQ-1). The Financial Operator commits a line before paying it; reversible until the first payment lands. Only committed lines accept attributions.
3. **Per-line payment attribution** — a new `DisbursementLineAllocation` join (`DisbursementId`, `ItemId`, `Amount`) realizing payment↔line M:N. Two new zero-colón blocking checks: split integrity (Σ line-allocations = disbursement amount) at Record/Edit, and per-line over-payment (Σ payments to a line ≤ its committed budget) re-checked at `Validar`, symmetric with P1's participant-level over-disbursement gate.
4. **6-dimension composed balances** — `ParticipantBalance` gains `Committed`; a new composed projection returns the balance tree per participant → tranche → line. `Available = Allocated − Paid` unchanged.

**Technical approach:** additive dacpac-only schema (1 new table `Tranches`, 1 new join table `DisbursementLineAllocations`, 2 nullable columns on `dbo.Items`), no new managed dependencies, reuse of the existing group-scoped Financial Operator role (spec 045) and reviewer funding-agreement surface (spec 040). Tranche setup is a new reviewer-side `TrancheController`/`ITrancheService`; commit + attribution extend the existing `DisbursementController`/`IDisbursementService`; the new line-level reconciliation is a pure `DisbursementLineReconciliation` domain service.

## Technical Context

**Language/Version**: C# / .NET 10 (LTS), EF Core 10
**Primary Dependencies**: ASP.NET MVC, .NET Aspire, ASP.NET Identity, SQL Server (dacpac). **No new managed dependencies** (Constitution: new NuGet requires spec approval — none needed here).
**Storage**: SQL Server via dacpac (`FundingPlatform.Database`), EF Core data-access only. New: `dbo.Tranches`, `dbo.DisbursementLineAllocations`; altered: `dbo.Items` (+`TrancheId`, +`CommitState`).
**Testing**: xUnit unit (`Tests.Unit`), integration against real SQL (`Tests.Integration`), Playwright E2E over the Aspire stack (`Tests.E2E`).
**Target Platform**: Linux server (Aspire-orchestrated container), es-CR culture.
**Project Type**: Server-rendered ASP.NET MVC web app, Clean Architecture (Domain / Application / Infrastructure / Web).
**Performance Goals**: Composed-balance projection for one application is a bounded set of small correlated queries (tranches ≤ tens, lines ≤ tens per application); no new N+1 on any list surface. Reuse P1's `AsNoTracking` read patterns.
**Constraints**: To-the-colón reconciliation (`decimal(18,2)`, zero tolerance, `MinDetectableDifference = 0.01`). Optimistic concurrency (`RowVersion`) on mutable entities. Group-overlap + executed-state 404 no-disclosure on every financial surface. No EF migrations (schema-first).
**Scale/Scope**: One additional entity + one join + two columns; ~2 new services, 1 new controller, ~6 new partials/views, 1 new pure domain service, ~6 new audit actions, 1 post-deploy role-seed already exists (no new role).

## Constitution Check

*GATE: evaluated before Phase 0 and re-checked after Phase 1 design. Version 1.1.0.*

| Principle | Status | Notes |
|---|---|---|
| **I. Clean Architecture** | ✅ PASS | Domain: `Tranche`, `DisbursementLineAllocation`, `ItemCommitState` enum, `DisbursementLineReconciliation` (pure), extended `ParticipantBalance`. Application: `ITrancheService`, extended `IDisbursementService`/`IParticipantBalanceProjection`, DTOs. Infrastructure: `TrancheService`, extended `DisbursementService`/`ParticipantBalanceProjection`, EF configs. Web: `TrancheController`, extended `DisbursementController`, views. Dependencies point inward. |
| **II. Rich Domain Model** | ✅ PASS | Tranche assignment mediated by the `Application` aggregate root (mirrors `AssignLineCodeToItem`); commit is a guarded `Item` state transition; split-integrity + per-line over-payment are domain invariants (pure service + service re-check). No anemic leakage. |
| **III. E2E (NON-NEGOTIABLE)** | ✅ PASS | Four independently testable user stories → filtered Playwright classes (US1 tranche setup, US2 commit, US3 attribution+over-payment, US4 filtering) + P1 regression. Delivery gate = filtered E2E green (per CLAUDE.md). |
| **IV. Schema-First DB** | ✅ PASS | All schema via `.sql` edits; nullable-column adds are inline (spec 032/037 precedent, no backfill); new tables follow the `ItemImpacts` join template. No EF migrations / `EnsureCreated`. |
| **V. Specification-Driven** | ✅ PASS | spec.md → this plan.md → tasks.md → implementation. User stories prioritized + independently testable. |
| **VI. Simplicity / YAGNI** | ✅ PASS | Derived tranche amount (no stored amount / no partition reconciliation); virtual default tranche (no migration, no lazy row); off-ledger commit status (no ledger-vocabulary growth — deferred to P6); explicit P3–P9 deferrals; reuse of existing role + controllers. |

**Quality gates (Development Workflow):** validation errors collected and shown together (mirrors P1 `List<DomainError>`); optimistic concurrency via `RowVersion` on `Tranche` and the existing entities; authorization verifies group overlap + role + executed-state on every mutation. **No violations → Complexity Tracking empty.**

## Project Structure

### Documentation (this feature)

```text
specs/046-tranches-budget-lines/
├── plan.md              # This file
├── research.md          # Phase 0 — resolves OQ-1/OQ-2/OQ-3 + freeze + default-tranche + reconciliation shape
├── data-model.md        # Phase 1 — entities, enums, VOs, tables, EF config, relationships
├── contracts/
│   └── interfaces.md     # Phase 1 — service interfaces, DTOs, controller routes, audit actions
├── quickstart.md        # Phase 1 — dev + test walkthrough
├── spec.md              # Feature spec (done)
├── REVIEW-SPEC.md       # Spec review (SOUND)
├── review_brief.md      # Reviewer guide
└── tasks.md             # Phase 2 — /speckit-tasks (NOT created here)
```

### Source Code (repository root)

```text
src/
├── FundingPlatform.Domain/
│   ├── Entities/
│   │   ├── Tranche.cs                         # NEW — per-application named phase; RowVersion
│   │   ├── DisbursementLineAllocation.cs      # NEW — (DisbursementId, ItemId, Amount) join
│   │   ├── Item.cs                            # EDIT — +TrancheId, +CommitState + guarded Commit/Uncommit/AssignTranche
│   │   └── Application.cs                     # EDIT — aggregate-mediated tranche CRUD + assignment + execution freeze guard
│   ├── Enums/
│   │   ├── ItemCommitState.cs                 # NEW : byte { Uncommitted=0, Committed=1 }
│   │   └── ReconciliationComparison.cs        # EDIT — +DisbursementSplitVsTotal=3, +LinePaymentVsBudget=4
│   ├── ValueObjects/
│   │   └── ParticipantBalance.cs              # EDIT — +Committed (5→6 dims)
│   └── Services/
│       └── DisbursementLineReconciliation.cs  # NEW — pure: split-integrity + per-line over-payment
├── FundingPlatform.Application/
│   ├── Tranches/
│   │   ├── ITrancheService.cs                 # NEW
│   │   ├── ITrancheQuery.cs                   # NEW — read model for reviewer + composition
│   │   └── TrancheDtos.cs                     # NEW
│   ├── Disbursements/
│   │   ├── IDisbursementService.cs            # EDIT — commands carry line splits; +Commit/Uncommit
│   │   ├── IParticipantBalanceProjection.cs   # EDIT — +GetComposedForApplicationAsync
│   │   ├── DisbursementDtos.cs                # EDIT — +LineAllocationInput; composed balance DTOs
│   │   └── DisbursementReasons.cs             # EDIT — +split/over-payment/commit reasons
│   └── Services/
│       └── ApplicationCurrencyTotal.cs        # EDIT — extract LineBudget(Item) per-item helper
├── FundingPlatform.Infrastructure/
│   ├── Services/
│   │   ├── TrancheService.cs                  # NEW — reviewer tranche CRUD + assignment (mirrors FundService)
│   │   ├── DisbursementService.cs             # EDIT — persist/validate splits; Commit/Uncommit; per-line over-payment re-check
│   │   └── ParticipantBalanceProjection.cs    # EDIT — composed tranche/line tree + Committed
│   └── Persistence/
│       ├── Configurations/
│       │   ├── TrancheConfiguration.cs        # NEW
│       │   ├── DisbursementLineAllocationConfiguration.cs  # NEW
│       │   └── ItemConfiguration.cs           # EDIT — CommitState HasConversion<byte>, TrancheId FK
│       └── AppDbContext.cs                    # EDIT — +DbSet<Tranche>, +DbSet<DisbursementLineAllocation>
├── FundingPlatform.Web/
│   ├── Controllers/
│   │   ├── TrancheController.cs               # NEW — [Authorize(Reviewer,Admin)] Route "Review/{applicationId:int}/Tranches"
│   │   └── DisbursementController.cs          # EDIT — +Commit/Uncommit; Record/Edit take splits; composed balance on Index
│   ├── Views/
│   │   ├── Review/
│   │   │   ├── Review.cshtml                  # EDIT — render _TrancheEditor when ShowReviewerChecklist
│   │   │   └── _TrancheEditor.cshtml          # NEW — assign items→tranches (data-searchable, spec 031)
│   │   └── Disbursement/
│   │       ├── Index.cshtml                   # EDIT — composed tranche/line panel + per-line commit + split form
│   │       ├── _TrancheBalancePanel.cshtml    # NEW
│   │       └── _BudgetLineRow.cshtml          # NEW
│   ├── ViewModels/
│   │   ├── Tranches/TrancheEditorViewModel.cs # NEW
│   │   └── Disbursements/DisbursementViewModels.cs  # EDIT
│   └── Resources/
│       ├── TrancheResources.cs                # NEW — es-CR
│       └── DisbursementResources.cs           # EDIT — commit/attribution/over-payment copy
├── FundingPlatform.Database/
│   ├── Tables/
│   │   ├── dbo.Tranches.sql                   # NEW
│   │   ├── dbo.DisbursementLineAllocations.sql  # NEW
│   │   └── dbo.Items.sql                      # EDIT — +TrancheId INT NULL FK, +CommitState TINYINT NOT NULL DF(0)
│   └── PostDeployment/ (no new script — Financial Operator role already seeded by 10_)
tests/
├── FundingPlatform.Tests.Unit/               # Tranche/commit invariants, DisbursementLineReconciliation, LineBudget helper
├── FundingPlatform.Tests.Integration/        # real-SQL: composed projection, split persistence, over-payment gate, filtered index races
└── FundingPlatform.Tests.E2E/                # US1–US4 + P1 regression
```

**Structure Decision**: existing 4-project Clean Architecture solution (`FundingPlatform.slnx`) with the mandated Domain/Application/Infrastructure/Web split plus the dacpac `Database` project and three test projects. P2 is purely additive within this structure — no new projects, no restructuring.

## Complexity Tracking

> No Constitution violations. Table intentionally empty.

The one arguable tension — loading financial responsibility (`CommitState` + `TrancheId`) onto the existing `Item` — is the ratified brainstorm decision (budget-line = `Item`); the alternative (a separate `BudgetLine` financial entity referencing `Item`) was rejected as duplicating line identity/price and requiring sync. Mitigation: commit/tranche concerns stay behind explicit guarded behavior methods on `Item`/`Application`, consistent with Principle II.
