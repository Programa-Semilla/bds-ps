# Deep Review Findings

**Date:** 2026-07-16
**Branch:** 047-evidence-graph-required-docs
**Rounds:** 1
**Gate Outcome:** PASS
**Invocation:** manual

## Summary

| Severity | Found | Fixed | Remaining (documented-accepted) |
|----------|-------|-------|--------------------------------|
| Critical | 0 | 0 | 0 |
| Important | 5 | 5 | 0 |
| Minor | 16 | 9 | 7 |
| **Total** | **21** | **14** | **7** |

**Agents completed:** 5/5 (correctness, architecture, security, production-readiness, test-quality). Security found nothing — the FinOp-write / Auditor+Admin-read-only posture, the flat-404 group-scope, the cross-application id guards, the Admin-only matrix, and the magic-byte upload gate all hold.
**External tools:** CodeRabbit + Copilot not installed (skipped).

## Findings

### FINDING-1 — ReplaceAsync could strand an over-allocation (FR-005)
- **Severity:** Important · **Confidence:** 85 · **Category:** correctness · **Source:** correctness-agent (also: test-quality)
- **File:** `src/FundingPlatform.Infrastructure/Services/EvidenceService.cs` (ReplaceAsync) · **Round:** 1 · **Resolution:** fixed (round 1)

**What was wrong:** `ReplaceAsync` re-validated with `lines: null`, so the `Σ allocations ≤ amount` guard was skipped. An operator could attach an invoice, allocate it to the ceiling, then Replace the amount *below* the allocated total — stranding an over-allocation the attach/allocate paths forbid.

**Why it matters:** Directly violates FR-005 and the spec edge case "reducing a document's amount below its already-allocated total must be refused." The stranded over-allocation corrupts every downstream per-line sum (LineAccepted, completeness, closure reconciliation).

**How resolved:** `ReplaceAsync` now reads `Σ EvidenceLineAllocations.Amount` for the evidence and refuses with `AllocationExceedsAmount` when it exceeds the new amount. Regression test `EvidenceGraphTests.Replace_AmountBelowAllocatedTotal_Refused` added; the US4 E2E was corrected (it previously attached 400k/allocated 400k then replaced to 350k — asserting an invalid state; now allocates 300k so the reduction stays valid).

### FINDING-2 — Zero-activity line could be closed (spec.md line 95)
- **Severity:** Important · **Confidence:** 75 · **Category:** correctness · **Source:** correctness-agent
- **File:** `src/FundingPlatform.Infrastructure/Services/BudgetLineClosureService.cs` (CloseAsync) · **Round:** 1 · **Resolution:** fixed (round 1)

**What was wrong:** With no attributed payment and no acceptance, `LinePaid == LineAccepted == 0` passed the equality leg trivially, so a line with no activity (under a rule set requiring no docs) could reach Closed.

**Why it matters:** The spec edge case (line 95) is explicit: "a budget-line with no attributed payment cannot satisfy the equality chain and therefore cannot be closed (it is completed via cancellation paths, not closure)." An admin can create a rule set requiring nothing, making the zero-activity path reachable.

**How resolved:** `CloseAsync` now refuses with a new `NoPaymentToClose` reason when `LinePaid < 0.01`. Integration test `ClosureGateTests.Close_NoValidatedPayment_Blocks` added. The closure E2E was reworked to seed a real validated payment (`EvidenceClosureSeeder`) + a matching acceptance so the happy close→reopen genuinely reconciles.

### FINDING-3 — Attach two-phase write could strand an orphaned evidence node (NFR-002)
- **Severity:** Important · **Confidence:** 70 · **Category:** production-readiness / correctness · **Source:** production-agent + correctness-agent
- **File:** `src/FundingPlatform.Infrastructure/Services/EvidenceService.cs` (AttachAsync) · **Round:** 1 · **Resolution:** fixed (round 1)

**What was wrong:** The evidence node + v1 committed in SaveChanges #1; the per-line allocation rows (which carry the orphan-guard invariant) committed only in SaveChanges #2. A crash between the two left a committed node with zero allocations — the orphaned state FR-007 forbids — for evidence with no disbursement anchor.

**Why it matters:** Violates NFR-002 (evidence/allocation writes must be transactional) and produces the exact orphan FR-007 declares impossible. Worse than the P1 `DisbursementService` two-SaveChanges pattern, which defers only the *audit* row (not invariant data) to the second save.

**How resolved:** Added `CompensateFailedAttachAsync` — on a SaveChanges #2 failure the just-committed node (+ owned version chain + blob) is removed, so the orphan-guard invariant is never left violated by a partial write.

### FINDING-4 — Untested closure leg (d): RequiredEvidenceNotFullyAllocated
- **Severity:** Important (test coverage) · **Confidence:** 90 · **Category:** test-quality · **Source:** test-quality-agent
- **File:** `tests/FundingPlatform.Tests.Integration/Evidence/ClosureGateTests.cs` · **Round:** 1 · **Resolution:** fixed (round 1)

**What was wrong:** FR-015 has four closure legs; only three (missing-doc, unvalidated-payment, equality-mismatch) had blocking tests. Leg (d) — a required graph document under-allocated — had no test at any level; deleting or inverting it would keep every test green.

**How resolved:** Added `ClosureGateTests.Close_RequiredEvidenceUnderAllocated_Blocks` (require Invoice; attach a graph Invoice of 100k allocated only 60k with a matching validated payment + acceptance so legs a–c + the payment guard pass; assert `RequiredEvidenceNotFullyAllocated`).

### FINDING-5 — AllocateAsync unhandled unique-index race → 500
- **Severity:** Important · **Confidence:** 70 · **Category:** production-readiness · **Source:** production-agent
- **File:** `src/FundingPlatform.Infrastructure/Services/EvidenceService.cs` (AllocateAsync) · **Round:** 1 · **Resolution:** fixed (round 1)

**What was wrong:** `AllocateAsync` caught only `DbUpdateConcurrencyException`. Two operators concurrently allocating the same `(EvidenceId, ItemId)` with no pre-existing row both INSERT; the `UX_EvidenceLineAlloc_Evidence_Item` unique index rejects the loser with a plain `DbUpdateException`, uncaught → 500 (the `Allocate` controller action, unlike Attach/Replace, has no surrounding try/catch).

**How resolved:** Added `catch (DbUpdateException)` returning the retryable es-CR concurrency error (mirrors `DocumentRuleService.UpsertAsync`).

### FINDING-6 — N+1 in RequiredEvidenceFullyAllocatedAsync
- **Severity:** Minor · **Confidence:** 90 (CONFIRMED) · **Category:** production-readiness · **Source:** production-agent
- **File:** `src/FundingPlatform.Infrastructure/Services/BudgetLineClosureService.cs` · **Round:** 1 · **Resolution:** fixed (round 1)

**What was wrong:** One `SumAsync` per required-typed evidence inside a `foreach`. **How resolved:** Replaced with a single grouped read (`GroupBy(EvidenceId).Select(Sum)` → dictionary), compared in memory.

### FINDING-7…10 — Dead code / stale comment cleanup
- **Severity:** Minor · **Category:** architecture · **Source:** architecture-agent · **Resolution:** fixed (round 1)
- Removed dead `EvidenceReasons.Codes.UploadFailed` (F7) and `DocRuleReasons.Codes.InvalidInput` + message (F8) — no producers.
- Corrected the stale `EnsureEvidenceUnlockedAsync` comment claiming closure "is inert until closure ships" — closure ships in this slice; the lock is live (F9).
- Removed the unused `IEvidenceService.GetVersionsAsync` (the version chain reaches the UI via `EvidenceDetail.Versions`) (F10).

### FINDING-11 — Duplicated line-label helpers that had diverged
- **Severity:** Minor · **Confidence:** 72 · **Category:** architecture · **Source:** architecture-agent · **Resolution:** fixed (round 1)
- **What:** Two identical private `LineLabel` helpers (EvidenceService + BudgetLineClosureService) plus a third format in the controller. **How resolved:** Extracted `Item.FormatLabel(lineCode, productName, itemId)` as the single source; both services now call it.

### FINDING-12 — Isolated the missing-required-doc closure test (leg a)
- **Severity:** Minor (test) · **Confidence:** 74 · **Category:** test-quality · **Resolution:** fixed (round 1)
- `Close_MissingRequiredDoc_Blocks` previously co-failed leg (c); now arranges paid == accepted so only leg (a) blocks.

---

## Remaining Findings (documented-accepted — no code change)

### FINDING-13 — Per-line invoice equality leg not checked for graph invoices (FR-024)
- **Severity:** Important-flagged · **Confidence:** 75 · **Category:** correctness · **Source:** correctness-agent
- **Decision:** ACCEPTED as designed, with the reachable hole now closed. FR-024 requires `Σ payment = Σ invoice = Σ acceptance` per line; the implementation checks `paid == accepted` and treats the invoice leg as inherited-by-construction for **disbursement-anchored** invoices (research **D6**: P1 forces disbursement↔invoice, P2 forces Σ split = disbursement, so per-line invoice coverage == LinePaid). **Graph** invoices are constrained by allocation-integrity (FR-005) + the fully-allocated closure leg (FR-015d). The residual case — a line with a validated payment AND a graph invoice whose allocation differs — is exactly the interpretation gap [REVIEW-PLAN.md](REVIEW-PLAN.md) flagged for reviewer sign-off ("is that transitivity argument airtight?"). Implementing a literal three-way graph-invoice equality risks double-counting graph vs disbursement invoices and would contradict D6. FINDING-2's `NoPaymentToClose` guard closes the previously-reachable no-payment path. **Deferred to reviewer** per the plan's flagged risk; P4/P6 will revisit if operators allocate graph invoices independently of the payment.

### FINDING-14 — Item has no RowVersion; concurrent double-close not detected
- **Severity:** Minor · **Confidence:** 70 (CONFIRMED) · **Category:** production-readiness · **Source:** production-agent
- **Decision:** ACCEPTED. Closure is off-ledger (FR-018) and `Item.Close` is idempotent, so a concurrent double-close cannot corrupt balances — the only effect is a duplicate `closure.line_closed` audit row + a re-stamp. Adding a `RowVersion` to `Items` would introduce optimistic-concurrency exceptions across **every** item-write flow (applicant draft edits, reviewer edits, tranche assignment, commit), a broad and risky change for a bounded off-ledger effect. Matches P2's documented-accepted precedent (`Item.CommitState` also has no RowVersion; the `Validar` money-gate re-reads fresh sums). The closure gate likewise re-reads fresh sums at close time.

### FINDING-15…21 — Minor architecture + test-hardening backlog
- **A2** (`GetCompletenessAsync` doc says "used by the closure UI" but the Index reads the projection directly) and **A4** (redundant fully-qualified type names in the AdminController DocumentRules block) — cosmetic; left as-is.
- Test-hardening backlog (behavior is covered elsewhere; these strengthen assertions): **T2** SC-007 out-of-group flat-404 not E2E-asserted for the evidence surface (role read-only IS covered; group-scope proven for the P1/P2 sibling surface); **T3** reopen "no balance change" asserts ledger-count, not a full balance-dimension snapshot; **T4** audit-trail rows (attach/close/reopen) not asserted in tests (the writer wiring is exercised, the row content is not); **T7** cascade-delete proven on InMemory only (no E2E delete flow); **T8** the AC-002/AC-003 multi-line M:N is proven at integration only (the E2E seed app has one line); **T9** the version-history E2E asserts version count + download but not the superseded row's reason/actor/hash rendering.

These are follow-up test additions, not defects; the corresponding production behavior is verified by the existing unit/integration/E2E suites.
