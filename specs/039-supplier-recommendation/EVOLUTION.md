# Evolution Log: Supplier Recommendation Algorithm Rewrite (spec 039)

Deviations and decisions taken during implementation, for the review-code gate.

## Algorithm / domain

- **Latent raw-`Price` → CRC fix applied (research D6).** The new `SupplierScore`
  price criterion compares `ConvertedCrcAmount ?? Price` (CRC-normalized), fixing
  the pre-existing mixed-currency bug where the old algorithm compared raw `Price`.
- **FR-043 (spec 013) rejected-supplier masking relocated.** The rewritten
  `SupplierScore` is verification-status-agnostic (it only knows CCSS eligibility,
  per spec 039 scope). To preserve "a Rejected supplier is never recommended", the
  mask now lives in `ReviewService` DTO mapping: `IsRecommended = score.IsRecommended
  && !supplierIsRejected`. `IsSupplierVerified`/`IsSupplierRejected` are sourced
  directly from `Supplier.VerificationStatus` (not from the score).
- **CCSS gate exception.** `Item.Approve` throws `SupplierIneligibleException`
  (new, `Domain/Exceptions/`) when the selected quotation's supplier is CCSS
  `sin inscripción`. `ReviewService.ReviewItemAsync` catches it and returns
  `UserFacingErrorCode.SupplierCcssSinInscripcion` with the provider name as
  `Detail`; the Web translator builds the templated es-CR message (Detail is data,
  not English copy — allowed by `UserFacingError`'s contract).

## Schema / data

- **T008 — no seed quotation rows to update.** The dacpac has zero
  `INSERT INTO dbo.Quotations`; quotations are created at runtime. So "update seed
  quotations with varied delivery/warranty" was N/A. The four new columns are
  `NOT NULL` with `DEFAULT (1)` (research D8) which covers any pre-existing rows on
  the persistent dev volume; the SC-001 non-lowest-price demo is exercised by
  E2E-created quotes (`SupplierRecommendationTests`).
- CHECK constraints added (`>0`, unit `IN (1,2)`).

## Web / UI

- **`_QuoteFields.cshtml`: HTML5 `min="1"` removed** from the delivery/warranty
  number inputs so a `0`/blank value reaches the authoritative server-side es-CR
  validator (FR-026) deterministically (the native `min` otherwise blocked submit
  client-side, hiding the server message). The domain `TimeDuration` + the VM
  `[Range(1,…)]` + the controller guard still reject ≤0.
- **Item-level messages inline.** "Ningún proveedor elegible" (FR-020) and
  "Selección manual requerida …" (FR-021) are rendered inline in `Review.cshtml`
  (es-CR), matching the codebase's existing inline-alert convention. The CCSS
  block message IS centralized in `IUserFacingErrorTranslator` (research D11).

## Tests

- **Edit-quotation existing tests** updated to pass matching delivery (30 días) /
  warranty (12 meses) in `EditQuotationCommand` so idempotency assertions still
  hold (the seeded quote carries the same values).
- **Tie behavior (FR-021) ripple.** Pre-existing E2E tests that encoded the OLD
  "tie → both recommended" behavior were rewritten to the new "tie → none
  recommended + manual selection" behavior:
  `SupplierEvaluationTests.US1_TiedScores_*` and
  `SupplierSelectionTests.EqualScores_NoneRecommended_ManualSelectionRequired`.
- **T022 (edit) vs T023 (add).** Integration tests cover the edit-path
  delivery/warranty validation + round-trip; the E2E `QuoteFieldsTests` covers the
  add-path rejection (SC-004) + acceptance. "Edit keeps required" is covered by the
  shared `_QuoteFields` partial + integration T022.
- **Call-site sweep (T010).** The `Quotation` ctor and `Item.AddQuotation` gained
  two required `TimeDuration` params; ~30 test/seed call sites across Unit +
  Integration + E2E were updated to pass them.

## Pre-existing failure (out of scope, NOT caused by spec 039)

- `ApplicantReusesVerifiedSupplierTests` AS01/AS02/AS03 fail at
  `AdminSupplierDetailPage.ToggleComplianceAllOnAsync()` waiting for
  `admin-supplier-einvoice-toggle` — a control **removed by spec 038** (e-invoice
  dropped from scoring). The test files are unchanged by spec 039 (empty `git diff`
  vs `main`); this is a dangling reference left by spec 038's test-infra cleanup.
  Fixing it (rewriting the stale toggle helper to the enum selects) is spec-038
  scope and was left untouched.

## Test results (this branch)

- Unit: 666/0 (added `TimeDurationTests`, rewrote `SupplierScoreTests`, added
  `ItemApproveEligibilityTests`; deep-review added a 3-way partial-tie test +
  blocked-provider zeroed-score asserts).
- Integration: 407/0 (added delivery/warranty validation + round-trip + warranty-zero
  in `ApplicationServiceEditQuotationTests`).

## Deep-review fixes (post-implementation gate)

The multi-agent deep review (0 Critical, 4 Important, 11 Minor) drove these fixes
(see `review-findings.md`): FR-019 finalize-time re-check (`ReviewService`); zero-quotation
items no longer show the "all CCSS-blocked" message; out-of-range `DurationUnit` POSTs return
an es-CR field error instead of a 500 (3 boundaries); warranty-zero + FR-022 per-provider
breakdown + 3-way-tie test coverage added; FR-043 downstream-mask documented in `SupplierScore`.
The XSS path (supplier name → block message) was traced and confirmed safe (Razor encoding).
- Filtered E2E green: `ItemFieldOrderTests` 1/1, `SupplierRecommendationTests` 1/1,
  `SupplierRecommendationTieTests` 1/1, `QuoteFieldsTests` 2/2,
  `SupplierRecommendationBlockTests` 2/2, `SupplierEvaluationTests` 6/6, plus the
  affected quote-create/edit + `SupplierSelection` classes (27/30 in that batch —
  the 3 failures are the pre-existing spec-038 e-invoice breakage above).
