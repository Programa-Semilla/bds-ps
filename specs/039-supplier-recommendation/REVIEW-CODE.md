# Code Review: Supplier Recommendation Algorithm Rewrite (spec 039)

---

## Code Review Guide (30 minutes)

This section guides a code reviewer through the implementation changes, focusing on
high-level questions that need human judgment. Spec compliance is 26/26 FRs; this
guide is about the decisions behind that, not the checklist.

**Changed files:** ~30 files — Domain (3 new: `DurationUnit`, `TimeDuration`,
`SupplierBlockReason`; 1 exception; `SupplierScore` rewrite; `Quotation`/`Item`
edits), Application (DTO, `ReviewService`, `ApplicationService`, `EditQuotationCommand`,
error code), Infrastructure (`QuotationConfiguration`), Database (`dbo.Quotations.sql`),
Web (2 VMs, `_QuoteFields`, `Item/Add`, `Review.cshtml`, 2 controllers, translator,
`DurationUnitLabels`), plus Unit/Integration/E2E tests.

### Understanding the changes (8 min)

- Start with [`SupplierScore.cs`](../../src/FundingPlatform.Domain/ValueObjects/SupplierScore.cs):
  the whole feature is this pure function. Read `ComputeForItem` top-to-bottom — the
  eligibility filter, the two tie rules, the strict-max recommendation.
- Then [`TimeDuration.cs`](../../src/FundingPlatform.Domain/ValueObjects/TimeDuration.cs)
  + [`Quotation.cs`](../../src/FundingPlatform.Domain/Entities/Quotation.cs): the new
  required quote fields the algorithm consumes.
- Question: the algorithm is computed live on every reviewer render
  ([research D7](research.md)). Is the per-render arithmetic cost acceptable, or
  should the result be memoized per request?

### Key decisions that need your eyes (12 min)

**Two distinct tie rules** (`SupplierScore.cs`, [FR-008](spec.md)/[FR-009](spec.md)/[FR-010](spec.md))

Price ties give all tied providers **1**; delivery/warranty ties give all tied **2**.
This asymmetry is the most error-prone part and is the client's explicit rule.
- Question: read the `priceTie` vs delivery/warranty branches — does the asymmetry
  match your reading of §14?

**FR-043 rejected-supplier masking moved to the service** (`ReviewService.cs` MapToReviewDto)

The new `SupplierScore` is verification-agnostic; "a Rejected supplier is never
recommended" (spec 013) is preserved by `IsRecommended && !isRejected` in the DTO
mapping. See [EVOLUTION.md](EVOLUTION.md).
- Question: is masking in the mapping layer (vs. the algorithm) the right home, given
  spec 039 deliberately scopes the algorithm to CCSS-only eligibility?

**CCSS gate placement** (`Item.Approve` in [`Item.cs`](../../src/FundingPlatform.Domain/Entities/Item.cs), [FR-019](spec.md))

The hard block is a domain guard throwing `SupplierIneligibleException`, translated to
es-CR in `ReviewService`/the Web translator (provider name passed as `Detail`).
- Question: is the domain the right place (un-bypassable), and is passing the provider
  name through `UserFacingError.Detail` acceptable (it's data, not English copy)?

**Price compared on CRC-normalized amount** (`SupplierScore.PriceKey`, [research D6](research.md))

This fixes a latent bug where the old algorithm compared raw `Price` across currencies.
- Question: the `ConvertedCrcAmount ?? Price` fallback — is the raw-`Price` fallback
  for a null-snapshot quote the right behavior, or should such quotes be excluded?

### Areas where I'm less certain (5 min)

- `Review.cshtml` breakdown cell ([FR-022](spec.md)): the seven-criterion breakdown is
  rendered as a stacked list inside a table cell. Is that legible enough, or should it
  be a popover / wider layout? UX judgment.
- `_QuoteFields.cshtml`: I removed HTML5 `min="1"` from the delivery/warranty inputs so
  `0`/blank reaches the server-side es-CR validator deterministically. Trade-off: the
  spinner now allows below 1 in the UI (server rejects). Acceptable?
- `SupplierScore` ordering: results are ordered eligible-first, then by total desc. The
  reviewer dropdown default selection uses `IsRecommended`. On a tie, nothing is
  pre-selected — confirm that's the desired reviewer experience.

### Deviations and risks (5 min)

- **FR-019 wording** (`UserFacingErrorTranslator.cs`): the block message names the
  provider («nombre») but not the item explicitly. The spec says "naming the offending
  item and provider". The reviewer acts on one item's Approve, so context is implicit.
  Question: is naming the provider sufficient, or should the item product name be added?
- **T008 N/A** ([EVOLUTION.md](EVOLUTION.md)): no seed quotation rows exist in the
  dacpac, so there was nothing to seed with varied delivery/warranty; the SC-001 demo is
  driven by E2E-created quotes. Acceptable?
- **Pre-existing failure**: `ApplicantReusesVerifiedSupplierTests` AS01-03 fail on the
  spec-038-removed `admin-supplier-einvoice-toggle` (dangling test reference, unchanged
  by spec 039). Left untouched — confirm it's out of scope.
- No deviations from [plan.md](plan.md)'s architecture were identified; all layers were
  touched along the planned seams.

---

## Deep Review Report

> Automated multi-perspective code review results. Summarizes what was checked,
> found, and what remains for human review.

**Date:** 2026-06-18 | **Rounds:** 1/3 | **Gate:** PASS

### Review Agents

| Agent | Findings | Status |
|-------|----------|--------|
| Correctness | 2 | completed |
| Architecture & Idioms | 7 | completed |
| Security | 1 | completed |
| Production Readiness | 1 | completed |
| Test Quality | 5 | completed |
| CodeRabbit (external) | - | skipped (not installed) |
| Copilot (external) | - | skipped (not installed) |

### Findings Summary

| Severity | Found | Fixed | Remaining |
|----------|-------|-------|-----------|
| Critical | 0 | 0 | 0 |
| Important | 4 | 4 | 0 |
| Minor | 11 | 4 | 7 |

### What was fixed automatically

- **FR-019 hardening** (`ReviewService.FinalizeReviewAsync`): added a finalize-time
  re-check so an application can never advance with a CCSS `sin inscripción` provider
  selected, even if the status flips after item approval (closes the SC-003 100% gap).
- **Misleading no-eligible message**: items with zero quotations no longer render the
  "all blocked by CCSS" message.
- **Boundary robustness**: out-of-range `DurationUnit` POSTs now return an es-CR field
  error instead of an unhandled 500 (3 boundaries).
- **Test coverage**: added warranty-zero rejection (integration + E2E), strengthened the
  FR-022 breakdown E2E to assert per-provider raw values, added a 3-way partial-tie unit
  test and blocked-provider zeroed-score assertions. Documented the FR-043 downstream mask.

### What still needs human attention

All Critical and Important findings were resolved. 7 Minor findings remain, all accepted
or documented (see [review-findings.md](review-findings.md)) — quality refactors
(tuple-dictionary, positional DTO, duplicated validation copy) and pre-existing spec-013
surface (unused branch param). None are blocking. Two items worth a reviewer's glance:

- The FR-019 block message names the **provider** but not the **item** explicitly
  (`UserFacingErrorTranslator.cs`). Is naming the provider sufficient, or should the item
  product name be added? (See [Code Review Guide](#code-review-guide-30-minutes).)
- The `ComputeForItem` intermediate tuple-dictionary ([review-findings.md](review-findings.md)
  FINDING/A1) — worth a future cleanup, not blocking.

### Recommendation

All Critical/Important findings addressed and re-verified (Unit 666/0, Integration 407/0,
filtered E2E green). Code is ready for human review with no known blockers; the remaining
Minor items are documented for optional follow-up.
