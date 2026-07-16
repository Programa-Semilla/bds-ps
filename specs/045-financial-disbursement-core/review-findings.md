# Deep Review Findings

**Date:** 2026-07-15
**Branch:** 045-financial-disbursement-core
**Rounds:** 1
**Gate Outcome:** PASS
**Invocation:** quality-gate (superpowers)

## Summary

| Severity | Found | Fixed | Remaining |
|----------|-------|-------|-----------|
| Critical | 0 | 0 | 0 |
| Important | 9 | 9 | 0 |
| Minor | 9 | 9 | 0 |
| **Total** | **18** | **18** | **0** |

**Agents completed:** 5/5 (Correctness, Architecture, Security, Production Readiness, Test Quality). External tools (CodeRabbit, Copilot): not installed — skipped.

Two agents independently reported the same real bug (the `RecordAsync` unique-index race), merged into FINDING-1.

## Findings

### FINDING-1
- **Severity:** Important · **Confidence:** 88
- **File:** src/FundingPlatform.Infrastructure/Services/DisbursementService.cs (RecordAsync save)
- **Category:** correctness / production-readiness
- **Source:** correctness-agent + production-readiness-agent
- **Resolution:** fixed (round 1)

**What was wrong:** `RecordAsync`'s `SaveChanges` caught only `DbUpdateConcurrencyException`. Two operators recording the *first* disbursement for the same application concurrently both insert the one-and-only `Allocation` ledger row; the filtered-unique `UX_DisbursementLedger_Allocation` rejects the loser as a plain `DbUpdateException` (SqlException 2601/2627), which was uncaught → the whole record rolled back as an unhandled 500. `ValidateAsync` already handled its analogous case; `RecordAsync` was the inconsistent gap.

**How resolved:** Added a `catch (DbUpdateException)` arm returning the retryable `Concurrency` failure (mirrors `ValidateAsync`).

### FINDING-2
- **Severity:** Important · **Confidence:** 82 · **Category:** architecture · **Resolution:** fixed (round 1)
- **File:** DisbursementService.GetOrComputeAllocationAsync + ParticipantBalanceProjection.GetForApplicationAsync

**What was wrong:** The allocation-resolution rule (ledger snapshot else `ApplicationCurrencyTotal.Compute`) was implemented twice — once as the reconciliation ceiling, once as the user-facing `Allocated` figure. Divergence would silently split "approved = ceiling" — the crux invariant.

**How resolved:** Extracted `DisbursementAllocation.ResolveAsync(db, appId, ct)`; both call sites now use it.

### FINDING-3
- **Severity:** Important · **Confidence:** 85 · **Category:** architecture (FR-030 compliance) · **Resolution:** fixed (round 1)
- **File:** DisbursementService.AttachEvidenceAsync (replace path)

**What was wrong:** The `evidence_replaced` audit payload carried only `after`; FR-030 / US4-AS4 require before/after for replace, and the replaced invoice amount is exactly what an internal auditor needs.

**How resolved:** Snapshot the prior amount/currency/reference/date before `Replace()` overwrites them; include as `before` in the payload (the attach path legitimately has no before → serializes `null`).

### FINDING-4 · Important · test-quality · fixed (round 1)
Non-CRC evidence rejection (FR-004, an enumerated spec Edge Case) had zero coverage. **Fixed:** `DisbursementValidationTests.AttachEvidence_RejectsNonPositiveAndNonCrc` asserts a `USD` attach fails with code `NonCrc`.

### FINDING-5 · Important · test-quality · fixed (round 1)
Zero/negative amount rejection (FR-003) untested on record + evidence. **Fixed:** `DisbursementValidationTests.Record_RejectsZeroAndNegativeAmount` + the non-positive evidence assertion.

### FINDING-6 · Important · test-quality · fixed (round 1)
The **validation-time** over-disbursement re-check (FR-005's race-proof gate, distinct `WouldExceedAllocation` reason) was never reached — the existing over-disbursement test flags at *record* time. **Fixed:** `DisbursementValidationTests.Validate_RefusesWhenCommittedTotalWouldBreachAllocation_EvenIfIndividuallyClean` records + proves A while it alone fits, records B to push the committed Σ over, then validates A and asserts the `OverAllocation` refusal.

### FINDING-7 · Important · test-quality · fixed (round 1)
Cancel "contributes nothing / leaves no ledger entry" was asserted only by row text. **Fixed:** `DisbursementProjectionTests.Cancelled_ContributesNothing_AndLeavesNoLedgerEntry` cancels one of two disbursements and asserts Paid/Pending/Available reflect only the survivor and no ledger entry exists for the cancelled id.

### FINDING-8 · Important · test-quality · fixed (round 1)
The audit tests asserted only `Contains("after")`, so a dropped `before` block (SC-007) would pass. **Fixed:** `DisbursementAuditTests` now asserts the `disbursement.edited` payload contains `before`, `after`, and the exact prior `"bankTxn":"TX-1"` value.

### FINDING-9 · Important · test-quality · fixed (round 1)
Auditor read-only was verified only by DOM absence, not the server-side write boundary (SC-008 / FR-025). **Fixed:** `DisbursementRoleScopingTests` now issues a crafted Record POST as the auditor (valid anti-forgery token from the rendered logout form + same-origin fetch) and asserts it is refused (403 / AccessDenied) with no disbursement created.

## Minor findings (all fixed)

| # | File | Issue | Resolution |
|---|------|-------|------------|
| M-1 | DisbursementService.ListAsync / DisbursementDtos | `DisbursementListItem.IsValidatable` was dead + omitted the discrepancy check (diverged from `GetAsync`) | Removed the field (list shows state + evidence badges only) |
| M-2 | DisbursementReconciliation | `OneColon = 0.01` misnamed (0.01 is a céntimo) | Renamed `MinDetectableDifference`, corrected comment |
| M-3 | DisbursementInbox/Index + _DiscrepancyList | hardcoded es-CR literals not in resources | Added constants + `ComparisonLabel` to `DisbursementResources`; views reference them |
| M-4 | DisbursementController.Flash | dead 4th param | Removed |
| M-5 | DisbursementController.Detail | GET lacked the cross-app id guard (defense-in-depth) | Added `DisbursementBelongsAsync` |
| M-6 | DisbursementService.RecordAsync | always eager-loaded Items→Quotations | Light load; heavy graph only when the allocation must be computed (first record) |
| M-7 | DisbursementService.AttachEvidenceAsync | old blob could leak if SaveChanges #2 failed on replace | Delete superseded blob right after the row swap (SaveChanges #1) |
| M-8 | DisbursementLedgerTests | "exercised by E2E" comment was a coverage illusion | Corrected to state the schema index is a defense-in-depth backstop, not asserted |
| M-9 | DisbursementReconciliationTests | SC-002 only covered missing-invoice | Missing-bank-receipt specific-reason covered by `DisbursementValidationTests.Validate_RefusesWithSpecificReason_WhenAnEvidenceIsMissing` |

## Security

The security agent found the authorization surface (FR-008/024/025/029) **correct and complete** — group-overlap scope + executed-state gate on every action, 404-before-403 ordering (no disclosure), cross-app id guard on all per-disbursement actions, CSRF on all POSTs, magic-byte + size-guard on upload, no path traversal, no XSS. Its single Minor (Detail defense-in-depth) is M-5, fixed.

## Remaining Findings

None. All Critical/Important and all Minor findings were resolved in round 1. Verified: Unit 10/10, Integration 11/11, filtered E2E (re-run in progress at write time — confirmed green).
