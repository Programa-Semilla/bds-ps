# Quickstart: Tranches & Budget-Lines (P2)

How to build, run, and exercise the feature end-to-end.

## Build & run

```bash
dotnet build FundingPlatform.slnx
dotnet run --project src/FundingPlatform.AppHost      # Aspire: Web + SQL (dacpac auto-deploys new tables/columns)
```

The dacpac diff adds `dbo.Tranches`, `dbo.DisbursementLineAllocations`, and the two `dbo.Items` columns automatically on AppHost startup (no manual migration; nullable/defaulted so existing rows are untouched).

## Roles / seed accounts (ephemeral E2E)

Reuse spec 045 seeds — **no new role or seed**:
- Reviewer (`reviewer@programa-semilla.test`) — defines tranches on the review surface.
- Financial Operator (group-scoped, seeded by `10_SeedFinancialOperatorRole.sql`) — commits lines, records/attributes/validates disbursements.
- Auditor + Admin — read-only on the financial surface.

## Manual walkthrough (the four user stories)

1. **US1 — tranche setup (reviewer).** Drive an application to `ResponseFinalized` with no agreement (the reviewer pre-audit surface, `Review/{id}`). In the **Tramos** card: create "Tramo 1"/"Tramo 2", assign line items to each. Confirm each tranche's derived amount = Σ its lines' budgets and Σ tranches = the allocation. Leave a line unassigned → it appears under the synthetic "General" tranche. Send to audit → execute the agreement → confirm tranche edits are now refused (frozen).
2. **US2 — commit (Financial Operator).** On `Applications/{id}/Disbursements`, commit two lines. Confirm **Committed** rises by their budgets at line/tranche/participant levels; un-commit one; try to un-commit a line after paying it → refused.
3. **US3 — attribution (Financial Operator).** Record a disbursement, splitting its amount across two committed lines (optionally in different tranches). Confirm the split must sum to the amount (mismatch → rejected), per-line Paid composes up, and validating a disbursement that over-pays a line is blocked. Over-payment shows negative Available (never clamped).
4. **US4 — filtering.** Filter budget-lines by tranche, status (Uncommitted/Committed/PartiallyPaid/Paid/Validated), supplier, and validation state.

## Tests (delivery gate = filtered E2E green)

```bash
dotnet test tests/FundingPlatform.Tests.Unit          # Tranche/Item invariants, DisbursementLineReconciliation, LineBudget
dotnet test tests/FundingPlatform.Tests.Integration   # real SQL: composed projection, split persistence, over-payment race, filtered indexes
dotnet test tests/FundingPlatform.Tests.E2E --filter "FullyQualifiedName~Tranche|FullyQualifiedName~BudgetLine|FullyQualifiedName~LineAttribution"
```

Per CLAUDE.md: run the **filtered** E2E classes exercising this change (US1–US4 + P1 `Disbursement*` regression), not the full ~30-min suite. Integration tests MUST hit real SQL — the `HasConversion<byte>()` `CommitState` mapping and the filtered-unique indexes are invisible to EF-InMemory (035/040/045 lesson).

## Gotchas (carried from P1 / house conventions)

- **TINYINT enum:** `Item.CommitState` and the new `ReconciliationComparison` values need `HasConversion<byte>()` or real-SQL materialization throws `Byte→Int32`.
- **Multiple-cascade-path:** `DisbursementLineAllocations` must be FK-CASCADE to `Disbursements` and FK-NO-ACTION to `Items` (two paths to `Applications`) — the `ItemImpacts` topology.
- **Derived, not stored:** tranche amount and budget-line status are computed, never persisted — don't add columns for them.
- **Freeze:** tranche structure is guarded on `State != AgreementExecuted` in the aggregate; commit/attribution are post-execution and intentionally not frozen.
- **No-disclosure:** every financial POST runs `GuardWriteAsync` (executed+group 404, then role 403) before doing work — never leak existence.
