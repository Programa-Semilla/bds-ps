# Implementation Plan: Full Reconciliation Engine

**Branch**: `048-full-reconciliation-engine` | **Date**: 2026-07-17 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `specs/048-full-reconciliation-engine/spec.md`
**Program slice**: Financial-execution **P4 of 9**. Depends on P1 (045), P2 (046), P3 (047) — all shipped.

## Summary

Turn the P1–P3 zero-colón reconciliation from ephemeral, computed-on-read hard blocks into **persisted, stateful discrepancies** with a fixed per-rule severity (Blocking / non-blocking Warning), a lifecycle (Open→Assigned→UnderCorrection→Resolved|Waived) with per-discrepancy correction history, a group→agency reconciliation dashboard, and a best-effort assignment email. The money guarantee is preserved by **persistence model C**: a wrapping `IReconciliationMaterializer` upserts persisted `Discrepancy` rows for visibility after each mutation, while the existing money gates keep recomputing fresh at the decision instant and throwing (untouched). Two additive dacpac tables (`Discrepancies`, `DiscrepancyEvents`), three new/extended byte enums, one new pure warning evaluator, a materializer + lifecycle service + group-scoped dashboard projection, one controller, and a direct-send email factory. No new managed dependencies; no new role (reuses Financial Operator). All open questions resolved in [research.md](./research.md).

## Technical Context

**Language/Version**: C# / .NET 10 (net10.0), EF Core 10
**Primary Dependencies**: ASP.NET MVC, .NET Aspire, ASP.NET Identity, SQL Server (dacpac), Playwright (E2E). **No new NuGet packages.**
**Storage**: SQL Server via dacpac (`FundingPlatform.Database`); EF Core data-access only. Two new tables.
**Testing**: xUnit unit + integration (real SQL via AspireFixture), Playwright E2E (Page Object Model)
**Target Platform**: Linux container (Aspire-orchestrated); es-CR default culture
**Project Type**: Server-rendered ASP.NET MVC web app, Clean Architecture (Domain/Application/Infrastructure/Web)
**Performance Goals**: reconciliation materialization is per-application and synchronous (in the mutating request); dashboard projection capped at `MaxRows=500`, group-scoped in-query. No new N+1 (batched reads, the P3 completeness-projection lesson).
**Constraints**: exact decimal (`DECIMAL(18,2)`, `0.01` tolerance, NFR-001); transactional per two-SaveChanges discipline (NFR-002); deterministic evaluators (NFR-020); money gates' fresh-recompute path unchanged (SC-004).
**Scale/Scope**: largest slice in the program — 4 user stories; ~2 tables, ~4 enums, ~6 new services/projections/factories, 1 controller, dashboard + per-app view changes.

## Constitution Check

*GATE evaluated against `.specify/memory/constitution.md` v1.1.0. Re-checked post-design.*

| Principle | Compliance |
|-----------|------------|
| **I. Clean Architecture** | ✅ Pure evaluator + entities in Domain; `IReconciliationMaterializer`/`IDiscrepancyLifecycleService`/`IReconciliationDashboardProjection`/`DiscrepancyReasons` in Application; EF impls + email factory + audit routing in Infrastructure; controller/views/`ReconciliationResources` in Web. Dependencies point inward. |
| **II. Rich Domain Model** | ✅ `Discrepancy` owns its lifecycle transitions with guards (`Waive` throws on Blocking; reason-required); `DiscrepancyEvent` append-only via the root; no anemic state manipulation in services. |
| **III. E2E Testing (NON-NEGOTIABLE)** | ✅ Four E2E classes mapped to US1–US4 in [quickstart.md](./quickstart.md), golden + error paths, POM; P1–P3 regression preserved (SC-004). |
| **IV. Schema-First dacpac** | ✅ Two additive `.sql` tables; no EF migrations; no post-deploy backfill (greenfield). TINYINT `HasConversion<byte>()` + materialization regression test. |
| **V. Specification-Driven** | ✅ spec → this plan → tasks (next). Four independently-testable stories; US1+US2 spine deliverable alone. |
| **VI. Simplicity / YAGNI** | ✅ Dropped FR-010(a) (OQ-5, non-computable); direct-send over an outbox `Assignee`-bucket extension; polymorphic scope key over 5 nullable typed FKs; tolerance parameter seam without a config UI (P5). Each deferral names its target slice. |
| **Quality gate — optimistic concurrency** | ✅ `Discrepancy.RowVersion` (FR-018), independent of the deferred `dbo.Items`-RowVersion debt. |
| **Quality gate — authorization ownership** | ✅ Per-discrepancy `GuardWriteAsync` (group-overlap flat-404 → read-only 403), reusing `IReviewerScope`/`ApplicantSharesAnyGroupAsync`. |
| **Quality gate — collect-all validation errors** | ✅ Materializer records ALL current discrepancies per run (not first-fail); dashboard shows the full set. |
| **Tech standards** | ✅ No new frameworks/packages; existing stack only. |

**Result: PASS — no violations. Complexity Tracking empty.**

One deliberate simplification worth flagging (not a violation): the polymorphic `(ScopeType, ScopeEntityId)` scope key has **no FK integrity** on `ScopeEntityId`. Justified in research D2 — the rows are engine-managed (only the materializer writes them), always recomputed from live data, and a stale scope id simply auto-resolves next run. This avoids the multiple-cascade-path dacpac publish failure (spec-029/035 lesson) that 5 nullable typed FKs to `Applications` would cause.

## Project Structure

### Documentation (this feature)

```text
specs/048-full-reconciliation-engine/
├── plan.md              # this file
├── research.md          # D0–D7, resolves OQ-1…OQ-5
├── data-model.md        # Discrepancy + DiscrepancyEvent, enums, indexes, SQL
├── contracts/
│   └── interfaces.md    # evaluator + materializer + lifecycle + projection + controller + email + audit
├── quickstart.md        # E2E verification plan → SCs
├── spec.md              # requirements (OQs resolved)
├── REVIEW-SPEC.md       # SOUND
├── review_brief.md
├── checklists/requirements.md
└── tasks.md             # /speckit-tasks output (NOT created here)
```

### Source Code (repository root)

```text
src/
  FundingPlatform.Domain/
    Enums/DiscrepancyState.cs            # NEW  (Open/Assigned/UnderCorrection/Resolved/Waived)
    Enums/DiscrepancyScopeType.cs        # NEW  (Document/Payment/BudgetLine/Participant/Tranche)
    Enums/DiscrepancySeverity.cs         # EDIT (add Warning=1)
    Enums/ReconciliationComparison.cs    # EDIT (add EvidenceDateAnomaly/PossibleDuplicatePayment/GraphInvoiceAllocationDrift)
    Entities/Discrepancy.cs              # NEW  aggregate root (owns _events; guarded transitions)
    Entities/DiscrepancyEvent.cs         # NEW  append-only child
    Entities/AdminAuditEvent.cs          # EDIT (discrepancy.* constants + TargetTypeDiscrepancy)
    Services/ReconciliationWarnings.cs   # NEW  pure warning evaluator (3 rules)
    ValueObjects/WarningDescriptor.cs    # NEW
  FundingPlatform.Application/
    Reconciliation/IReconciliationMaterializer.cs        # NEW
    Reconciliation/IDiscrepancyLifecycleService.cs       # NEW
    Reconciliation/IReconciliationDashboardProjection.cs # NEW (+ DTOs, ReconciliationFilter)
    Reconciliation/DiscrepancyReasons.cs                 # NEW (es-CR refusal strings)
  FundingPlatform.Infrastructure/
    Services/ReconciliationMaterializer.cs               # NEW (wraps evaluators; upsert/auto-resolve/insert)
    Services/DiscrepancyLifecycleService.cs              # NEW (assign/under-correction/waive; audit+email)
    Persistence/ReconciliationDashboardProjection.cs     # NEW (group-scoped, EvidenceInboxProjection shape)
    Persistence/Configurations/DiscrepancyConfiguration.cs        # NEW
    Persistence/Configurations/DiscrepancyEventConfiguration.cs   # NEW
    Persistence/AppDbContext.cs          # EDIT (2 DbSets)
    Audit/AdminAuditEventWriter.cs       # EDIT (discrepancy. prefix branch)
    Email/DiscrepancyAssignmentEmailFactory.cs           # NEW (direct-send, best-effort)
    Services/DisbursementService.cs      # EDIT (call MaterializeAsync after mutations)
    Services/EvidenceService.cs          # EDIT (call MaterializeAsync after mutations)
    Services/BudgetLineClosureService.cs # EDIT (call MaterializeAsync after Close/Reopen)
    DependencyInjection.cs               # EDIT (register new services)
  FundingPlatform.Web/
    Controllers/ReconciliationDashboardController.cs     # NEW (/Reconciliation)
    Views/Reconciliation/Index.cshtml + Detail.cshtml + _Filters + _SummaryTiles  # NEW
    Views/Disbursement/_DiscrepancyList.cshtml           # EDIT (bind persisted rows, severity badge, deep-link)
    Views/Emails/DiscrepancyAssignment.cshtml + .text.cshtml  # NEW
    Resources/ReconciliationResources.cs                 # NEW
    Resources/DisbursementResources.cs                   # EDIT (ComparisonLabel 5–7, SeverityLabel/Badge)
    (sidebar partial)                                    # EDIT (reconciliation entry, 3 roles)
  FundingPlatform.Database/
    Tables/dbo.Discrepancies.sql         # NEW
    Tables/dbo.DiscrepancyEvents.sql     # NEW
tests/
  FundingPlatform.Tests.Unit/           # ReconciliationWarnings*, Discrepancy aggregate transitions
  FundingPlatform.Tests.Integration/    # DiscrepancyEnumMaterialization, unique-identity index, cascade, materializer upsert/auto-resolve
  FundingPlatform.Tests.E2E/            # ReconciliationPersistence, DiscrepancyLifecycle, ReconciliationDashboard, DiscrepancyAssignmentNotification
```

**Structure Decision**: Existing 4-layer Clean Architecture. Discrepancy is a standalone Application-scoped aggregate (P1/036 precedent) — no navigation added to `Application`. Reconciliation logic stays pure in Domain; persistence/orchestration in Infrastructure; read-model in a group-scoped projection.

## Implementation Phasing (for `/speckit-tasks`)

1. **Foundation** — enums, `Discrepancy`+`DiscrepancyEvent` entities + EF configs + dacpac tables + DbSets + materialization regression test. (No behavior yet.)
2. **US1 (P1) — detection + severity + persistence**: `ReconciliationWarnings` evaluator, `ReconciliationMaterializer` (wrap existing evaluators, upsert/auto-resolve/insert), wire `MaterializeAsync` into the mutating services; extend `_DiscrepancyList` to show persisted rows + severity. **Checkpoint A.**
3. **US2 (P1) — lifecycle + history**: `Discrepancy` transition methods (already on the entity from Phase 1), `IDiscrepancyLifecycleService` (assign/under-correction/waive), `discrepancy.*` audit family, auto-resolve/reopen via the materializer, concurrency. **Checkpoint B (spine complete: land US1+US2 together, regression green).**
4. **US3 (P2) — dashboard**: `IReconciliationDashboardProjection` (group-scoped) + `ReconciliationDashboardController` + views (tiles, filter toolbar, detail + timeline) + sidebar + `ReconciliationResources`. **Checkpoint C.**
5. **US4 (P3) — notification**: `DiscrepancyAssignmentEmailFactory` (direct-send, best-effort) wired into `AssignAsync`. **Checkpoint D.**

Each checkpoint: run the story's filtered E2E + the P1–P3 regression (SC-004) before proceeding.

## Complexity Tracking

*No constitution violations — table intentionally empty.*
