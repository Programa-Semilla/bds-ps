# Deep Review Findings

**Date:** 2026-06-18
**Branch:** 039-supplier-recommendation
**Rounds:** 1
**Gate Outcome:** PASS
**Invocation:** manual (chained from review-code gate)

## Summary

| Severity | Found | Fixed | Remaining |
|----------|-------|-------|-----------|
| Critical | 0 | 0 | 0 |
| Important | 4 | 4 | 0 |
| Minor | 11 | 4 | 7 (accepted/documented) |
| **Total** | **15** | **8** | **7** |

**Agents completed:** 5/5 (correctness, architecture, security, production-readiness, test-quality).
**External tools:** CodeRabbit + Copilot — not installed in this environment (skipped).

Notable clears (verified, not findings): the supplier-name → block-message → reviewer
**XSS path is NOT vulnerable** (Razor auto-encoding at `_NotificationToasts.cshtml`; JS uses
`textContent`); the 4 NOT NULL + DEFAULT(1) columns are **migration-safe**; the `OwnsOne`
`TimeDuration` mapping agrees with the schema; Supplier/owned-types are eager-loaded (no
N+1 / null-deref); `ComputeForItem` per-render cost is negligible.

## Findings

### FINDING-1 — FR-019 not re-checked at finalize (status flip after approval)
- **Severity:** Important · **Confidence:** 72 · **Category:** correctness
- **File:** `ReviewService.cs` (`FinalizeReviewAsync`) · **Source:** correctness-agent · **Resolution:** fixed (round 1)

**What is wrong:** The CCSS `sin inscripción` block was enforced only at per-item
`Item.Approve`. If a provider's CCSS status flips to `sin inscripción` *after* an item was
approved with it (a slice-A live admin edit), `FinalizeReviewAsync` had no re-check and
would advance the application — defeating SC-003's "100% of attempts" guarantee.

**How it was resolved:** Added a pre-`Finalize` scan in `FinalizeReviewAsync` over every
approved item's selected provider; if any is `SinInscripcion`, it returns
`SupplierCcssSinInscripcion` (provider name in Detail) and does not finalize. Data is
already eager-loaded. Verified by the full E2E finalize suite staying green.

### FINDING-2 — "ningún proveedor elegible" message fired for zero-quotation items
- **Severity:** Important · **Confidence:** 72 · **Category:** architecture
- **File:** `ReviewService.cs` (`hasAnyEligible`) + `Review.cshtml` · **Source:** architecture-agent · **Resolution:** fixed (round 1)

**What is wrong:** `hasAnyEligible = scoreMap.Values.Any(IsEligible)` is `false` for an item
with **no** quotations, so the view rendered "todos los proveedores … bloqueados por la CCSS"
— conflating "empty" with "all-CCSS-blocked" (FR-020 is about all *candidates* being blocked).

**How it was resolved:** `hasAnyEligible = scoreMap.Count == 0 || scoreMap.Values.Any(IsEligible)`
so the block message only fires when candidates exist but none are eligible.

### FINDING-3 — warranty-zero rejection path untested (SC-004 says delivery OR warranty)
- **Severity:** Important · **Confidence:** 80 · **Category:** test-quality
- **Files:** integration + E2E · **Source:** test-quality-agent · **Resolution:** fixed (round 1)

**What is wrong:** Only the zero-*delivery* rejection was tested; the symmetric zero-warranty
guard had no coverage, so a regression dropping/mis-keying it would pass.

**How it was resolved:** Added `EditQuotation_ZeroWarranty_ReturnsValidationFailed`
(integration) and `AddQuote_ZeroWarranty_RejectedWithEsCrMessage` (E2E).

### FINDING-4 — FR-022 breakdown only shallowly asserted (recommended row only)
- **Severity:** Important · **Confidence:** 75 · **Category:** test-quality
- **File:** `SupplierRecommendationTests.cs` · **Source:** test-quality-agent · **Resolution:** fixed (round 1)

**What is wrong:** The E2E asserted the seven labels + two raw values only on the recommended
row. SC-002 requires the breakdown "for each eligible provider." A label-only check passes
even if values are blank/wrong/identical.

**How it was resolved:** Added assertions that the non-recommended (cheap) row's breakdown is
visible with its own discriminating raw values (60 días / 6 meses) and total.

### FINDING-5 — tampered out-of-range DurationUnit → unhandled 500 instead of es-CR
- **Severity:** Minor · **Confidence:** 85 · **Category:** security (robustness)
- **Files:** `SupplierController.cs` (add + reuse), `ApplicationService.cs` (edit) · **Source:** security-agent · **Resolution:** fixed (round 1)

**What is wrong:** A crafted POST with `DeliveryLeadTimeUnit=99` (value valid) passed the
value check, then `new TimeDuration(...)` threw an unhandled `ArgumentException` → generic
HTTP 500 (not the es-CR message FR-026 wants). NOT a persistence bypass — the VO
`Enum.IsDefined` guard + the DB CHECK both block it; this is a robustness/UX defect.

**How it was resolved:** Added `Enum.IsDefined` unit checks alongside the value checks at all
three boundaries, surfacing an es-CR field error instead of a 500.

### FINDING-6 — rejected-mask split between algorithm and mapper (FR-043)
- **Severity:** Minor · **Confidence:** 80 · **Category:** architecture
- **File:** `SupplierScore.cs` / `ReviewService.cs` · **Source:** architecture-agent · **Resolution:** fixed (documented, round 1)

**What is wrong:** `SupplierScore` is verification-agnostic; the "Rejected → never recommended"
rule lives downstream in `MapToReviewDto`. A reader of the algorithm could wrongly assume it
is self-contained.

**How it was resolved:** Added an explicit scope note to the `SupplierScore` XML-doc pointing
to the downstream mask. (Feeding verification into the algorithm was considered but rejected —
spec 039 deliberately scopes the algorithm to CCSS-only eligibility.)

### FINDING-7 — 3-way partial tie not unit-tested
- **Severity:** Minor · **Confidence:** 70 · **Category:** test-quality · **Resolution:** fixed (round 1)

**How it was resolved:** Added `ThreeWayPartialTie_OnlyTopTwoFlagged_LowerNotFlagged` —
asserts the two top providers carry `IsTiedAtTop` and the lower one does not.

### FINDING-8 — blocked-provider per-criterion scores not asserted zeroed
- **Severity:** Minor · **Confidence:** 70 · **Category:** test-quality · **Resolution:** fixed (round 1)

**How it was resolved:** Extended `CcssSinInscripcion_ExcludedFromScoring…` to assert
`PriceScore/DeliveryLeadTimeScore/WarrantyTimeScore == 0` and `IsTiedAtTop == false`.

## Remaining Findings (accepted / documented — not blocking)

- **C2 — `Item.Approve` null-Supplier silent pass** (correctness, Minor). A defensive throw was
  tried but reverted: it broke the established test pattern where in-memory quotations don't
  carry the `Supplier` nav, and it changes the method contract. The production review flow
  always eager-loads `Supplier`; a clarifying comment was added. Accepted.
- **A1 — `ComputeForItem` intermediate 9-field tuple-dictionary** (architecture, Minor). A
  quality refactor (build `SupplierScore` records up front + `with`-expressions). Deferred —
  pure-refactor risk not justified post-verification; behavior is covered by the unit matrix.
- **A3 — duration validation copy duplicated across VM `[Range]` / controller / service / VO**
  (architecture, Minor). Defence-in-depth by design; the es-CR strings could be centralized
  later. Accepted.
- **A4 — `ReviewQuotationDto` ~30-param positional record** (architecture, Minor). Convert to
  init-properties. Deferred (mechanical, touches an established DTO; all call sites use named
  args already).
- **A6 — unused `SupplierBranch?` param in `ComputeForItem`** (architecture, Minor). Pre-existing
  spec-013 signature ("reserved for display"); removing it is out of spec-039 scope. Accepted.
- **A7 — `EditQuotationCommand.WarrantyUnit=Months` default inert on edit path** (architecture,
  Minor). Harmless; the edit path always hydrates units from the persisted quote. Accepted.
- **P1 — `MapToReviewDto` computes the ordered score list then re-sorts the DTOs** (production-
  readiness, Minor). Negligible cost at the bounded cardinality (a few quotations/item).
  Accepted.
- **T5 — no integration test for the reviewer-decision CCSS exception→es-CR mapping**
  (test-quality, Minor). Covered by E2E (`SupplierRecommendationBlockTests`). A faster
  integration test would be nice-to-have. Accepted.
