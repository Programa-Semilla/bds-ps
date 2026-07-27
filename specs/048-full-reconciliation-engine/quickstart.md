# Quickstart / Verification: Full Reconciliation Engine (spec 048)

E2E is the primary quality gate (constitution III). Tests run against the Aspire-orchestrated stack via `AspireFixture` (ephemeral SQL, dacpac-deployed). Filter to these classes for the delivery gate; the P1–P3 money-gate regression (`Disbursement*`/`Tranche*`/`BudgetLine*`/`Evidence*`) must stay green (SC-004).

Seeds reuse `FundingAgreementSeeder.SeedExecutedAgreementAsync` (an `AgreementExecuted` application) + the P1–P3 disbursement/evidence seeders. Financial Operator, Auditor, Admin demo users exist (`@programa-semilla.test`, allowlisted for mail capture).

## E2E classes → user stories / SCs

### `ReconciliationPersistenceTests` (US1, SC-002/SC-003)
- **Blocking persisted + blocks:** record a disbursement whose invoice is 72 CRC under paid → assert a persisted Blocking `Discrepancy` (comparison `DisbursementVsInvoice`, expected/actual/−72) exists on the dashboard and that `Validar` is refused. *(SC-003 blocking side, US1 AC-1.)*
- **Clean → none:** matched amounts → no open discrepancy; validate succeeds. *(US1 AC-2.)*
- **Warning persisted + does NOT block:** two disbursements same supplier+amount+date → persisted Warning (`PossibleDuplicatePayment`); validation proceeds. *(US1 AC-3, SC-003 warning side.)*
- **Date anomaly warning:** invoice dated after payment → Warning `EvidenceDateAnomaly`. *(US1 AC-4.)*
- **Graph-invoice drift warning:** validated line payment vs divergent graph-invoice allocation → Warning `GraphInvoiceAllocationDrift`. *(US1 AC-5, 047 FINDING-13.)*
- **TINYINT round-trip** proven on real SQL by materialization (also covered by `DiscrepancyEnumMaterializationTests` integration).

### `DiscrepancyLifecycleTests` (US2, SC-001/SC-002/SC-006)
- **Assign keeps identity across re-run:** assign an open discrepancy (state Assigned, assignee set, timeline row); trigger an unrelated re-materialization (edit another field) → same discrepancy still Assigned, not reset to Open. *(SC-001, US2 AC-1/edge "re-run stability".)*
- **Auto-resolve on fix + reopen on recurrence:** correct a Blocking mismatch → next materialization auto-Resolves (row kept, timeline `Resolved`, system actor); reintroduce the mismatch → reopens same identity, prior timeline intact. *(SC-002, US2 AC-2/AC-6.)*
- **Waive a Warning:** waive with a required reason → state Waived, reason in timeline, `discrepancy.waived` audit written. *(SC-006, US2 AC-3.)*
- **Cannot waive Blocking:** waive attempt on a Blocking discrepancy → refused. *(SC-006, US2 AC-4.)*
- **Waived warning reopens on amount change:** waive a duplicate-payment warning, then edit the amount → reopens. *(US2 AC-5/edge.)*
- **Concurrency:** two lifecycle edits on the same discrepancy → second gets the concurrency refusal (RowVersion). *(edge "concurrent lifecycle edits", FR-018.)*

### `ReconciliationDashboardTests` (US3, SC-005)
- **Group scoping:** discrepancies in groups A & B; FinOp in A sees only A; Admin sees both; Auditor in A sees A read-only (no assign/waive controls). *(SC-005, US3 AC-1/2/3.)*
- **Filters:** filter by severity=Warning + supplier → list narrows; verify each FR-023 facet. *(US3 AC-4.)*
- **Detail:** open a row → expected/actual/difference/source/participant/line/severity/required-action + timeline shown. *(US3 AC-5.)*
- **Accessibility:** severity/status conveyed by text+icon, not color alone. *(US3 AC-6, FR-025.)*
- **Money-gate race:** introduce a mismatch after the last materialization, then validate → still blocked by the fresh recompute (not the stale snapshot). *(SC-003/SC-004, edge "money-gate race".)*

### `DiscrepancyAssignmentNotificationTests` (US4, SC-007)
- **Notify on assignment:** assign to an allowlisted operator → exactly one branded email captured (smtp4dev) to that operator. *(SC-007, US4 AC-1.)*
- **No detection spam:** materialize new discrepancies without assigning → no email. *(SC-007, US4 AC-2.)*

### Regression (SC-004) — run filtered
`Disbursement*`, `Tranche*`, `BudgetLine*`, `Evidence*` (P1–P3 SC-006 family) stay green — the money gates are untouched.

## Local run
```bash
# focused unit (pure evaluators + aggregate transitions)
dotnet test tests/FundingPlatform.Tests.Unit --filter "FullyQualifiedName~Reconciliation|FullyQualifiedName~Discrepancy"
# integration (real SQL — TINYINT materialization, unique-identity index, cascade)
dotnet test tests/FundingPlatform.Tests.Integration --filter "FullyQualifiedName~Discrepancy|FullyQualifiedName~Reconciliation"
# E2E (delivery gate)
dotnet test tests/FundingPlatform.Tests.E2E --filter "FullyQualifiedName~Reconciliation|FullyQualifiedName~Discrepancy"
```

## Delivery bar (CLAUDE.md)
A story is delivered only when its filtered E2E class is personally executed and green. Land **US1 + US2 as one checkpoint** (spine), then US3, then US4 — keeping the P1–P3 regression green at each (per the P3 largest-slice guidance).
