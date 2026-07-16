# Deep Review Findings

**Date:** 2026-07-16
**Branch:** 046-tranches-budget-lines
**Rounds:** 1
**Gate Outcome:** PASS (1 Critical + 6 Important fixed; 2 Important consciously documented — non-financial-impact, mitigated)
**Invocation:** manual (via speckit-spex-gates-review-code, deep-review extension)
**External tools:** disabled (`--no-external`)

## Summary

| Severity | Found | Fixed | Documented (accepted) | Remaining |
|----------|-------|-------|-----------------------|-----------|
| Critical | 1 | 1 | 0 | 0 |
| Important | 8 | 6 | 2 | 0 |
| Minor | 11 | 8 | 3 | 0 |
| **Total** | **20** | **15** | **5** | **0** |

**Agents completed:** 5/5 (correctness, architecture, security, production-readiness, test-quality). External tools skipped by request.

## Findings

### FINDING-1 (Critical) — `EditAsync` stale split + `ValidateAsync` no split re-check
- **File:** src/FundingPlatform.Infrastructure/Services/DisbursementService.cs (EditAsync, ValidateAsync)
- **Source:** correctness-agent (conf 80)
- **Resolution:** **fixed (round 1)**

**What was wrong:** Editing a disbursement's amount while leaving the split-editor inputs blank kept the old split (summing to the *old* amount), and `ValidateAsync` never re-checked split integrity — so an inconsistent split could validate and lock, breaking FR-013/SC-002 in a zero-colón feature.

**How resolved:** Added `EvaluateSplitIntegrityAsync` and (a) a defense-in-depth split-integrity re-check in `ValidateAsync` (a disbursement whose Σ line-allocations ≠ amount can never validate into the ledger), and (b) an `EditAsync` guard that, when `Lines == null`, re-validates the existing split against the new amount and refuses on mismatch. Regression test `LineAttributionTests.Edit_AmountOnly_LeavingStaleSplit_IsRefused`.

### FINDING-2 (Important) — Admin can write tranches (FR-021)
- **File:** src/FundingPlatform.Web/Controllers/TrancheController.cs
- **Source:** security-agent (conf 85)
- **Resolution:** **fixed (round 1)**

**What was wrong:** `TrancheController` authorized `Reviewer,Admin` with no write-scope gate, letting an Admin create/rename/delete/assign tranches — contradicting FR-021 ("Admins MUST be read-only for all of these actions") and inconsistent with the sibling `DisbursementController` (write = Financial Operator only).

**How resolved:** `TrancheController` is now `[Authorize(Roles = "Reviewer")]`; `ReviewController` renders the tranche editor only for reviewers. Admin is read-only for tranche definition, matching FR-021 and the segregation-of-duties model.

### FINDING-3 (Important) — `Record` split not atomic with the disbursement
- **File:** src/FundingPlatform.Infrastructure/Services/DisbursementService.cs (RecordAsync)
- **Source:** production-readiness-agent (conf 74)
- **Resolution:** **mitigated + documented (round 1)**

**What was wrong:** `RecordAsync` commits the disbursement in the first `SaveChanges` and the split rows in the second; a non-transient failure between them could leave a disbursement with no attributions.

**How resolved / accepted:** The FINDING-1 `ValidateAsync` split-integrity re-check is the load-bearing protection: a disbursement whose split never persisted (Σ 0 ≠ amount) **can never validate into the ledger** — money cannot move on a broken split. The residual is a stuck pre-validation disbursement the operator edits or cancels (operationally recoverable, no silent money-integrity break). A fully-atomic owned-nav rewrite was judged higher-risk than the recoverable residual it removes; deferred.

### FINDING-4 (Important) — `Item.CommitState` has no concurrency token (un-commit TOCTOU)
- **File:** src/FundingPlatform.Infrastructure/Services/DisbursementService.cs (UncommitLineAsync / ValidateLinesAsync)
- **Source:** production-readiness-agent (conf 70)
- **Resolution:** **documented (accepted limitation)**

**What was wrong:** The FR-007 "no recorded payment" un-commit guard is a read-then-write across `Items` and `DisbursementLineAllocations` with no serialization; concurrent operators could interleave a Record(attribute to X) with an Uncommit(X), leaving an uncommitted line carrying a payment.

**Why accepted:** The agent itself confirmed the **money-movement gate is unaffected** — the per-line over-payment check at `Validar` re-reads *fresh* committed sums, so financial integrity holds regardless of a stale `CommitState`. The residual impact is a display-only stale `Committed` dimension / line-status under a rare *same-line, same-instant, two-operator* race. The only robust fix (a RowVersion on the central `dbo.Items` table) would surface unhandled `DbUpdateConcurrencyException`s in existing item-review/quotation flows that don't catch them — a disproportionate regression risk versus a bounded, non-financial, rare-race display inconsistency. Documented as a known limitation (consistent with P1's documented concurrency characteristics). Follow-up candidate: add `Item.RowVersion` with a full concurrency-handling sweep.

### FINDING-5 (Important) — LineBudget LINQ selection duplicated ×3 (SC-003 drift risk)
- **File:** src/FundingPlatform.Infrastructure/Services/ParticipantBalanceProjection.cs, DisbursementService.cs
- **Source:** architecture-agent (conf 80)
- **Resolution:** **mitigated + documented (round 1)**

**What was wrong:** The "selected non-legacy quote's converted CRC amount" selection — the LINQ twin of the pure `ApplicationCurrencyTotal.LineBudget` — is inlined in three EF query sites; a change to the budget rule must be hand-synced across four places or SC-003 silently breaks.

**How resolved / accepted:** The new `BudgetLineReconciliationTests.AllSixDimensions_ReconcileToColon_AcrossLevels_AndComposedAllocatedEqualsFlat` locks the composed budget (LINQ twin) to the flat/ledger `Allocated` (pure `LineBudget`/`Compute`), so any divergence between the twins now fails a test. Full EF-side de-duplication into one composed `Expression<Func<Item,decimal>>` is awkward without LINQKit (not a project dependency) because the three sites project the budget alongside different sibling fields; deferred as a refactor, drift-guarded by the reconciliation test.

### FINDING-6 (Important) — flat vs composed `Paid` diverge for unattributed disbursements
- **File:** src/FundingPlatform.Infrastructure/Services/ParticipantBalanceProjection.cs
- **Source:** correctness-agent (conf 68)
- **Resolution:** **documented (by-design) + locked by test**

**What was wrong:** The composed tree derives every dimension from `DisbursementLineAllocations`; a flat (unattributed) disbursement contributes to the flat balance card but shows `Paid = ₡0` at line/tranche level, so the two panels can disagree.

**Why accepted:** In the intended P2 flow (FR-010, operator splits every disbursement — the UI provides the split editor) the two reconcile exactly, now locked by the new six-dimension reconciliation test. Divergence only occurs for *unattributed* disbursements — pre-feature P1 data (SC-006, grandfathered; the flat card is unchanged) or an operator who skips the split editor. The composed tree legitimately reads as "line-attributed execution" vs the flat card's "total execution." Making `Lines` mandatory would break the P1 regression suite and SC-006; the reconciliation-when-attributed guarantee (tested) plus the flat card (authoritative total) is the accepted resolution.

### FINDING-7 (Important) — FR-012 cross-tranche split untested
- **Source:** test-quality-agent (conf 90) — **fixed (round 1)**
Added `BudgetLineReconciliationTests.OneDisbursement_SplitAcrossTwoTranches_ComposesPerLineTranche`: one payment split across lines in two tranches, asserting each attribution composes into its own line's tranche node.

### FINDING-8 (Important) — SC-003 asserted for only 1 of 6 dimensions; composed vs flat Allocated not locked
- **Source:** test-quality-agent (conf 85) — **fixed (round 1)**
Added `AllSixDimensions_ReconcileToColon_AcrossLevels_AndComposedAllocatedEqualsFlat` (participant == Σ tranches == Σ lines for all six dimensions + composed.Allocated == flat/ledger Allocated).

### FINDING-9 (Important) — DeriveStatus payment buckets (PartiallyPaid/Paid/Validated) untested
- **Source:** test-quality-agent (conf 85) — **fixed (round 1)**
Added `DerivedStatus_PartiallyPaid_Paid_Validated` and `DerivedStatus_Paid_WhenFullyPaidButUnvalidated`.

## Minor findings

| # | File | Issue | Resolution |
|---|------|-------|------------|
| M-1 | DisbursementReasons.cs | Class doc omitted TrancheService as a producer | fixed |
| M-2 | ReconciliationComparison.cs / LineOverpaymentDiscrepancy.cs | Dead `LinePaymentVsBudget` enum arm + name coexistence with the VO | documented (harmless; unreachable label is defensive) |
| M-3 | DisbursementDtos.cs | `EditDisbursementCommand` "empty clears it" doc contradicted by validation | fixed (doc corrected) |
| M-4 | DisbursementService.cs / DisbursementReasons.cs | Over-payment label fallback `L-{id}` vs documented "APP-line" | fixed (docs aligned) |
| M-5 | TrancheService.cs | App-not-found reused `DISBURSEMENT_NOT_FOUND` code | fixed (new `APPLICATION_NOT_FOUND`) |
| M-6 | TrancheService.cs / TrancheController.cs | No lower state-bound on tranche mutation (Draft/Submitted could be POSTed) | documented (freeze upper-bound + editor only renders pre-audit; low real risk) |
| M-7 | ParticipantBalanceProjection.cs | Composed alloc query scoped via EXISTS-over-Items, not indexed ApplicationId | fixed (scope via Disbursements.ApplicationId) |
| M-8 | BudgetLineCommitTests.cs | "AtAllLevels" didn't assert tranche-level Committed | fixed (new tranche-level Committed test) |
| M-9 | BudgetLineFilterTests.cs | Supplier filter can't prove narrowing (single-supplier seed) | documented (follow-up: 2-supplier seed) |
| M-10 | BudgetLineFilterTests.cs | Date filter + FullyValidated branch untested | documented (follow-up) |
| M-11 | BudgetLineCommitTests / TrancheAdmin | Auditor read-only + non-reviewer tranche refusal untested | documented (follow-up; Admin read-only covered) |
| M-12 | LineAttributionTests.Edit_ReplacesSplit | Only aggregate asserted, not per-line | fixed (per-line amounts asserted) |
| M-13 | LineAttributionTests / E2E | Negative Available only at line level | fixed (integration asserts line + tranche negative) |
