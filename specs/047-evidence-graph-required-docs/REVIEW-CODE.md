# Code Review — Evidence Graph & Required-Document Rules (spec 047)

See [spec.md](spec.md), [plan.md](plan.md), and the full [review-findings.md](review-findings.md).

---

## Deep Review Report

> Automated multi-perspective code review results. This section summarizes
> what was checked, what was found, and what remains for human review.

**Date:** 2026-07-16 | **Rounds:** 1/3 | **Gate:** PASS

### Review Agents

| Agent | Findings | Status |
|-------|----------|--------|
| Correctness | 4 | completed |
| Architecture & Idioms | 7 | completed |
| Security | 0 | completed |
| Production Readiness | 4 | completed |
| Test Quality | 9 | completed |
| CodeRabbit (external) | — | skipped (not installed) |
| Copilot (external) | — | skipped (not installed) |

### Findings Summary

| Severity | Found | Fixed | Remaining |
|----------|-------|-------|-----------|
| Critical | 0 | 0 | 0 |
| Important | 5 | 5 | 0 |
| Minor | 16 | 9 | 7 (documented-accepted) |

### What was fixed automatically

- **Data-integrity guards (correctness):** `ReplaceAsync` now refuses shrinking an evidence amount below its allocated total (FR-005); `CloseAsync` refuses closing a line with no validated payment (spec.md line 95) — both were reachable holes that could strand an over-allocation or close a zero-activity line.
- **Transactional safety (production):** `AttachAsync` compensates (deletes node + version + blob) if its second SaveChanges fails, so a partial write can't leave an orphaned evidence node (NFR-002); `AllocateAsync` now catches the unique-index `DbUpdateException` race (was a 500).
- **Efficiency:** removed an N+1 in the closure gate's required-evidence-fully-allocated check (one grouped query).
- **Hygiene:** removed dead reason codes + an unused interface method, corrected a stale comment, and extracted one shared `Item.FormatLabel` (three diverging line-label formats collapsed to one).
- **Test coverage:** added the previously-untested closure leg (d) `RequiredEvidenceNotFullyAllocated`, a no-payment-close block, and the amount-shrink refusal; isolated the missing-doc closure test; reworked the closure E2E to seed a real validated payment.

### What still needs human attention

- **The graph-invoice equality leg (FR-024).** The per-line equality checks `paid == accepted`; the invoice leg is inherited-by-construction for disbursement-anchored invoices (research D6) and graph invoices are constrained by allocation-integrity + the fully-allocated closure leg. FINDING-2's no-payment guard closes the reachable hole. **Does the reviewer accept D6's interpretation, or should P3 add an explicit per-line graph-invoice equality term** (weighing the double-counting risk against disbursement invoices)? This is the risk [REVIEW-PLAN.md](REVIEW-PLAN.md) already flagged for sign-off.
- **`Item` has no RowVersion (double-close).** Accepted as off-ledger/bounded, matching P2's `CommitState` precedent. **Is a duplicate `closure.line_closed` audit row under a concurrent double-close acceptable until P8 adds segregation-of-duties**, or should `Items` carry a concurrency token despite the cross-flow churn?

Minor test-hardening items (SC-007 out-of-group 404 E2E, full balance-snapshot on reopen, audit-row content assertions, an E2E delete/multi-line flow) are catalogued in [review-findings.md](review-findings.md) as follow-ups; the corresponding behavior is verified by the existing suites.

### Recommendation

All Critical/Important findings addressed; the two documented-accepted items are design decisions (one already flagged in the plan) rather than defects. Verification after the fix round: **Unit 804/0, Integration 522/0, filtered E2E 6/0** (`EvidenceGraphAllocation`/`RequiredDocMatrixCompleteness`/`BudgetLineClosure`/`EvidenceVersionHistory`) + P1/P2 `Disbursement*` regression 17/0. Code is ready for human review; reviewers should weigh in on the two questions above.
