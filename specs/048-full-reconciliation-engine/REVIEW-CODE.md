# Code Review Gate — Full Reconciliation Engine (spec 048)

**Verdict: PASS** (after one fix round). Code implements the spec; all four user stories delivered and
independently E2E-verified on real SQL; P1–P3 money-gate behavior preserved (SC-004).

## FR compliance

| FR | Requirement | Evidence |
|----|-------------|----------|
| FR-001 | Reconciliation runs on every relevant mutation | `MaterializeAsync` wired into `DisbursementService` (Record/Edit/Attach/Validate/Cancel/Commit/Uncommit), `EvidenceService` (Attach/Replace/Allocate/Delete), `BudgetLineClosureService` (Close/Reopen). |
| FR-002 | Results persisted as discrepancy records | `ReconciliationMaterializer` upserts `Discrepancy` rows; own `SaveChanges` after the domain save. |
| FR-003 | Stable identity (scope-type, scope-entity-id, comparison); present→update, absent→auto-resolve, new→insert | `UX_Discrepancies_Identity`; `Detect`/`Refresh`/`AutoResolve` reconcile loop; rows retained never deleted. |
| FR-004 | Money gates recompute fresh + block, independent of the snapshot | `ValidateAsync`/`CloseAsync` still call the pure evaluators directly and throw; the materializer is best-effort visibility-only (persistence model C). Proven by the passing P1–P3 regression + `RecordWithMismatchedInvoice…BlocksValidation`. |
| FR-005 | Every rule carries a tolerance param, default 0 CRC | `Discrepancy.ToleranceApplied` (default 0); admin config Out of Scope. |
| FR-006 | Lifecycle Open→Assigned→UnderCorrection→Resolved\|Waived | `DiscrepancyState` + guarded aggregate transitions. |
| FR-007 | Assign / mark-under-correction | `IDiscrepancyLifecycleService.AssignAsync`/`MarkUnderCorrectionAsync`; terminal-state guarded (review M1). |
| FR-008 | Waive Warnings only (reason + audit) | `WaiveAsync` refuses Blocking + blank reason; `discrepancy.waived` audit. |
| FR-010 | Three Warning conditions (date anomaly, duplicate payment, graph-invoice drift) | `ReconciliationWarnings` (3 pure rules) + unit tests. |
| FR-011 | Auto-resolve on fix; no manual resolve/reopen | materializer `AutoResolve`; no service Resolve/Reopen verb. |
| FR-016 | Recurrence auto-reopens; a waiver reopens on amount change | `AutoReopen` (Resolved recurrence) + `Refresh` (Waived amount change). |
| FR-018 | Optimistic concurrency | `Discrepancy.RowVersion`; `DbUpdateConcurrencyException` → retryable refusal. |
| FR-021–024 | Group→agency dashboard: tiles, roll-ups, filterable list, detail + timeline | `IReconciliationDashboardProjection` + `ReconciliationDashboardController` + views. |
| FR-023 | Filters: participant, tranche, budget-line, supplier, date, severity, responsible, state | `ReconciliationFilter` + `_FilterToolbar`. |
| FR-025 | Severity/status never colour-alone | `SeverityLabel`+`SeverityIcon`+text badges everywhere. |
| US4 | Best-effort assignment email | `DiscrepancyAssignmentEmailFactory` inline in `AssignAsync` (log-and-continue). |

## Constitution
Clean Architecture layering respected (pure evaluator + entities in Domain; interfaces + reasons in
Application; EF impls + email + audit in Infrastructure; controller/views/resources in Web). Rich domain
model (guarded transitions, append-only child via root). Schema-first dacpac (2 additive tables, TINYINT
`HasConversion<byte>()` + real-SQL materialization test). E2E per story (non-negotiable) — all green.

## Deviations (logged in tasks.md)
- Views live under `Views/ReconciliationDashboard/` (controller-name match), not `Views/Reconciliation/`
  (the plan's folder label) — required for MVC view resolution.
- `/EvidenceGraph`-style dev seam: `/Dev/SeedDiscrepancy` (Development-only) added to drive the
  lifecycle/dashboard/notification E2E deterministically without constructing the complex warning
  conditions through the UI (mirrors the spec-043 dev-seam precedent).
- Integration tests use InMemory (spec-036/047 precedent); real-SQL enforcement (TINYINT materialization,
  unique identity index, CASCADE) proven by the E2E suite.
- Execution-date anchor for the date-anomaly rule uses `FundingAgreement.GeneratedAtUtc` (a robust,
  always-present conservative proxy — a document dated before the agreement was even generated is
  unambiguously anomalous).

## Deep review
See `review-findings.md` — 7 findings, 3 fixed (S1 delete/archive visibility on detail; M1 terminal-state
guard; M3 dedup hardening), 4 documented-accepted (correct-by-design or consistent-with-shipped).
