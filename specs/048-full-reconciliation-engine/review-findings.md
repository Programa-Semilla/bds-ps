# Deep Review — Full Reconciliation Engine (spec 048)

Two-agent focused review (materializer/aggregate correctness + controller/projection security) over the
spec-048 diff, after implementation. 7 findings; **3 fixed**, **4 documented-accepted**.

## Security + authorization review (`ReconciliationDashboardController` / `ReconciliationDashboardProjection` / `DevController`)

| # | Sev | Finding | Disposition |
|---|-----|---------|-------------|
| S1 | **Medium** | `GetDetailAsync` queried `_db.Applications` directly, bypassing `ExcludeDeleted`/`ExcludeArchivedFund`. A discrepancy on a soft-deleted / archived-fund app (hidden from the list) was still readable **and mutable** via `/Reconciliation/{id}` — the write guard authorizes solely via `GetDetailAsync` returning non-null. | **FIXED** — route the owning application through the query filter; a filtered-out app → flat 404 (blocks read + write). |
| S2 | Low | `OperatorOptionsAsync` exposes every Financial Operator agency-wide to a group-scoped caller; `Assign` accepts any `assigneeUserId` (no membership check on the target). Minor PII disclosure + you can assign to an out-of-group operator. | **Accepted** — not a caller scope-escalation (the write is still group-guarded); assignee-list group-scoping is a P4 minor deferral. The assignee simply gets a notification for a discrepancy in a group they may not act on. |
| S3 | Info | `/Dev/SeedDiscrepancy` is a mutating `[HttpGet]` under an `[AllowAnonymous]` controller. | **Accepted** — correctly gated by `IsDevelopment()` (404 in prod), mirrors the sibling spec-043 dev seams; dev-only, engine-managed row. |

**Verified clean:** out-of-group IDOR blocked (Detail + GuardWriteAsync both go through the group-overlap check → flat 404); 404-before-403 ordering (no existence disclosure); all 3 POSTs carry `[ValidateAntiForgeryToken]`; every DevController action 404s outside Development; admin short-circuit + empty-group early-return + applicant-UserId group-overlap consistent between list and detail; `CanWrite()` restricts writes to Financial Operator only.

## Materializer + aggregate correctness review (`ReconciliationMaterializer` / `Discrepancy`)

| # | Sev | Finding | Disposition |
|---|-----|---------|-------------|
| M1 | **Medium** | `Assign` / `MarkUnderCorrection` had no terminal-state guard and did not clear `ResolvedAt`/`WaivedReason`. The detail view's `canAct` only excluded `Resolved`, so a **Waived** row still showed the assign form → a user could re-activate a waived warning, leaving `WaivedReason` stale and bypassing the "no manual reopen" rule (FR-016). | **FIXED** — `Discrepancy.IsTerminal` guard throws in both methods; the lifecycle service pre-checks and returns a clean `NotActionable` refusal (no 500 on a crafted POST); the view's `canAct` now excludes `Waived` too. Unit tests added. |
| M2 | Medium | `Detect` always persists `ToleranceApplied = 0`. | **Accepted** — FR-005 mandates a **0-CRC default** tolerance; admin-configurable tolerance is explicitly Out of Scope (P5). The column is the forward seam; 0 is the correct current value. |
| M3 | Medium | `persisted.ToDictionary(identity)` would throw on a duplicate persisted identity, and the best-effort catch would then never self-heal. | **FIXED** — first-wins `TryAdd` dedup. (The `UX_Discrepancies_Identity` unique index already makes a duplicate impossible on real SQL; this is a defensive hardening.) |
| M4 | Low | The app-level `TotalVsAllocation` (comparison 2) leg is only evaluated when ≥1 non-cancelled disbursement exists. | **Accepted** — correct by construction: with zero disbursements `Σ(0) − allocation < 0`, which never flags an over-allocation (under-allocation is not a discrepancy, FR-005). No missed discrepancy. |
| M5 | Low | Arbitrary `First()`/`FirstOrDefault()` when resolving bank/invoice amounts and the duplicate-payment supplier. | **Accepted** — a disbursement carries at most one BankReceipt + one Invoice (matches the shipped `DisbursementService` pattern exactly); the duplicate-supplier `First()` is acceptable heuristic imprecision for a Warning-tier starter-set rule. |

**Verified correct:** `Difference = Actual − Expected` consistent in Detect + Refresh (no sign error); the `TotalVsAllocation` dictionary-collapse to one Participant row is intentional and lossless; AutoReopen-then-Refresh ordering for a recurring Resolved row fires exactly one `Reopened` event; the Waived amount-change reopen computes `amountChanged` before mutating and clears both `ResolvedAt` + `WaivedReason`; `AutoResolve` no-ops on terminal rows.

## Post-fix verification
Unit **828/0** (+3 terminal-guard tests), Integration reconciliation **13/0**, filtered reconciliation E2E green, P1–P3 regression E2E **12/12** (SC-004).
