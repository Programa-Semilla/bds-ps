---
name: 21-quotation-edit
description: Brainstorm — add a per-quotation in-place Edit surface on Application/Edit so applicants can fix Price/Currency/ValidUntil/SupplierBranch without losing the quotation's identity or AI-comparison artifact.
metadata:
  type: brainstorm
  status: spec-created
  spec: specs/023-quotation-edit/
---

# Brainstorm: Per-quotation In-place Edit

**Date:** 2026-05-20
**Status:** spec-created
**Spec:** specs/023-quotation-edit/

## Problem Framing

`/Application/Edit/{id}` exposes no per-quotation edit affordance. The `QuotationController` only ships `Convert` (add), `Replace` (swap file), and `Delete`. Two real-user scenarios are blocked:

1. **Pre-submit typo fix.** Applicant on a Draft Application notices the price they typed on a quotation is wrong. Today they must `Delete` and re-`Convert`, which discards the original `CreatedAt`, breaks audit continuity, and forces re-uploading the same PDF.
2. **Reviewer-returned correction.** Reviewer sends an Application back citing an error on a specific quotation (wrong amount, wrong validity, wrong branch). The applicant has the same Delete/re-Convert workaround, which also orphans any reviewer comments that referenced the original quotation row.

Surfaced post-spec-021 against URL `https://localhost:7080/Application/Edit/1003` ("there is no edit button so I can fix the price or anything before submission or in any other stage of the process while the reviewer has feedback and finds an error i.e."). The domain already exposes the primitives — `Quotation.EditAmount(price)` and `Quotation.ChangeCurrencyAsync(currency, conversion)` (spec 015) — but no controller endpoint or view ever consumed them.

## Approaches Considered

### A: Dedicated `Quotation/Edit` page reusing the Supplier/Add quote-fields form via a shared partial (CHOSEN)
- **Pros:** Mirrors the project's prevailing per-aggregate-edit pattern (`Item/Edit`). Reuses the existing `quote-conversion-preview.js` wiring. Single shared partial keeps create + edit in lockstep with no drift. Deep-linkable URL — emailable from a reviewer-feedback flow (OQ-3). Easy E2E coverage per the Constitution's E2E gate.
- **Cons:** Refactor cost on a working surface (Supplier/Add). Mitigated by SC-005 explicitly asserting the existing Supplier/Add suite stays green.

### B: Inline accordion on the existing `Application/Edit` listing
- **Pros:** No new route. Edit context stays on the listing surface.
- **Cons:** Heavier client-side JS. Validation error rendering is messier inside an accordion. No deep-linkable URL. Higher E2E maintenance.

### C: Modal on `Application/Edit`
- **Pros:** Fastest perceived flow for a quick price fix.
- **Cons:** No deep-link surface. Validation errors inside modals are recurring UX pain. Doesn't fit reviewer-email "click here to fix this quotation" CTAs (OQ-3).

## Decision

**Approach A.** Dedicated `Quotation/Edit` page rooted at `/Application/{appId}/Item/{itemId}/Quotation/{quotationId}/Edit`. The quote-fields portion of Supplier/Add gets extracted into a shared Razor partial (working name `_QuotationFieldsForm.cshtml`) and consumed by both surfaces with zero behavior change to Supplier/Add.

### Key resolutions during brainstorm

- **Lifecycle gate.** Edit allowed only when `Application.Status ∈ {Draft, ReturnedForChanges}`. Affordance hidden elsewhere; direct POST returns HTTP 422 with es-CR copy `"El estado de la solicitud cambió, recarga la página."`. Matches the two stakeholder scenarios above.
- **Editable fields.** All four: `Price`, `Currency`, `ValidUntil`, `SupplierBranch`. Switching to a **different supplier** is out of scope (Delete + re-Convert); branch picker restricted to branches of `Quotation.SupplierId`.
- **Actor.** Application owner (Applicant role) only. Admin / Reviewer / SupplierAdmin editing intentionally deferred.
- **Persistence routing.** Currency unchanged → `EditAmount(newPrice)` re-applies the pinned snapshot. Currency changed → `ChangeCurrencyAsync(newCurrency, conversion)` then `EditAmount(newPrice)`; consumes a fresh `ExchangeRate` snapshot and marks the rate used (spec 015 FR-008). Branch / ValidUntil persist directly.
- **AI comparison cache.** Successful Edit silently invalidates the per-Item `ComparisonArtifact` cache key (spec 020). No applicant-facing notice. Reviewer's next *Generar todo* regenerates on cache miss.
- **Concurrency.** Last-write-wins. No optimistic concurrency token in v1; matches Item/Edit precedent. Constitution OC gate addressed in `plan.md` Complexity Tracking per REVIEW-SPEC R-1.
- **`LegacyNeedsReview`-flagged rows** stay on the spec-015 admin-only path; Edit affordance hidden + POST rejected with 422.
- **File replace stays on the existing `Replace` endpoint.** OQ-1 leaves room to fold it into Edit later if reviewers ask.
- **Form reuse via extracted partial** rather than copy-paste, so Supplier/Add and Quotation/Edit cannot drift over time. SC-005 protects the refactor.

## Open Threads

- **OQ-1:** Include a "Replace file" affordance on the Edit page for one-stop editing? Default: no (Replace stays on the row).
- **OQ-2:** Emit an `AdminAuditEvent` for applicant-initiated quotation edits, or stay silent like Item/Edit? Default: silent for v1.
- **OQ-3:** When the reviewer's `RETURNED_TO_APPLICANT` email cites a specific quotation, should the CTA deep-link to `Quotation/{id}/Edit`? Defer to spec 021 email-template touch-up.
- **R-1 (plan-phase):** Justify the last-write-wins decision in `plan.md` Complexity Tracking under the Constitution's "OC for concurrent edit risk" gate, or add a rowversion token. Single-actor / two-tabs-same-user scope is the counterargument.
- **R-2 (plan-phase):** Per-user-story Playwright E2E task list (golden path for US1/US2/US3 at minimum) to satisfy Constitution III.
- **R-3 (plan-phase):** Confirm server returns the full `ModelState` validation collection on the Edit POST rather than fail-fast (Constitution quality gate).
- Whether the shared partial's final file name (`_QuotationFieldsForm.cshtml`) is right, or `_QuotationFields.cshtml` / `_QuotationInputs.cshtml` reads cleaner — pin during planning.
- Whether the AI-cache invalidation should also flush any in-flight `ComparisonJob` for this Item (race window during reviewer-side regeneration) — pin during planning.
- Branch this work merges back into: created off `021-feedback-session-may13` working tree; tracked as a session task to merge 023 back into 021 (not main) when complete.
