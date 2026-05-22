# Review Brief: In-place Quotation Field Edit

**Spec:** specs/023-quotation-edit/spec.md
**Generated:** 2026-05-20

> Reviewer's guide to scope and key decisions. See full spec for details.

---

## Feature Overview

`/Application/Edit/{id}` exposes no per-quotation edit affordance. Applicants who notice a typo before submit — or need to apply a reviewer-requested correction after the Application is returned — must Delete + re-attach the same PDF, which loses the original `CreatedAt`, breaks audit continuity, and forces re-uploading a document they already have. This spec adds a per-quotation Edit surface that mutates `Price`, `Currency`, `ValidUntil`, and `SupplierBranch` in-place while the Application is `Draft` or `ReturnedForChanges`, and reuses the existing Supplier/Add create-quote form via an extracted shared partial.

## Scope Boundaries

- **In scope:** Editable fields Price / Currency / ValidUntil / SupplierBranch on the Application owner's quotations in Draft + ReturnedForChanges; Supplier/Add quote-fields form extracted into a shared partial; AI comparison cache silent-invalidate; Edit affordance on Application/Edit (and Item/Edit if it lists quotations).
- **Out of scope:** Editing the PDF file on this surface (existing Replace endpoint remains); switching to a different Supplier (Delete + re-Convert); editing on UnderReview / Approved / FundingAgreement-issued states; Admin / Reviewer / SupplierAdmin editing; optimistic concurrency tokens; editing `LegacyNeedsReview`-flagged quotes; new `AdminAuditEvent` for applicant edits.
- **Why these boundaries:** The bug surfaced on a Draft application and the May-13 stakeholder loop names only the applicant-owns-the-Application case. Other actors and other states had no concrete user need. Cross-supplier swap is materially a different aggregate change.

## Critical Decisions

### D-1 — Reuse Supplier/Add form via extracted partial
- **Choice:** Extract the existing Price / Currency / ValidUntil controls (plus the `quote-conversion-preview.js` wiring) into a shared Razor partial; consume from both Supplier/Add (create) and the new Quotation/Edit page.
- **Trade-off:** Refactor cost on a working surface vs. living with form drift. Refactor wins because Supplier/Add E2E suite is robust enough to catch regressions (SC-005).
- **Feedback:** Confirm Supplier/Add E2E coverage is rich enough to safeguard the extraction.

### D-2 — Silent AI comparison cache invalidation
- **Choice:** A successful Edit invalidates the `ComparisonArtifact` cache key for the Item. No applicant-facing notice; reviewer regenerates on demand via *Generar todo*.
- **Trade-off:** A reviewer could theoretically read a stale artifact before regenerating. Alternative options were "notify reviewer" (banner) or "block edit if artifact exists" (hard stop).
- **Feedback:** Acceptable risk given the regenerate-on-demand workflow already in spec 020?

### D-3 — Last-write-wins concurrency
- **Choice:** No optimistic concurrency token. Matches Item/Edit precedent. Concurrent edit risk is two-tabs-same-user.
- **Trade-off:** Constitutionally the "OC for concurrent edit risk" gate suggests adding a token. Single-actor scope is the counterargument.
- **Feedback:** Plan-phase decision per Review R-1 — explicit Complexity Tracking entry.

## Areas of Potential Disagreement

### Constitution: optimistic concurrency
- **Decision:** Skip OC tokens.
- **Why this might be controversial:** Constitution 1.0.0 mandates OC for entities with concurrent edit risk.
- **Alternative view:** Add a rowversion column on Quotation and surface a 409 Conflict on stale POST.
- **Seeking input on:** Is single-actor / two-tabs-same-user below the constitutional threshold, or do we hold the line?

### Reusing Supplier/Add vs. building a focused Edit form
- **Decision:** Reuse via partial extraction.
- **Why this might be controversial:** Supplier/Add is a multi-step flow (supplier lookup → branch picker → quote fields); only the quote-fields slice is reused. The extraction has its own complexity.
- **Alternative view:** Standalone Edit form that doesn't share code with Supplier/Add — simpler change, but introduces drift risk over time.
- **Seeking input on:** Are stakeholders comfortable with a refactor on a working surface?

### Lifecycle gate scope
- **Decision:** Allow only on Draft + ReturnedForChanges; deny everywhere else (incl. UnderReview).
- **Why this might be controversial:** A reviewer reading an application "live" could still benefit from a corrected quote arriving mid-review.
- **Alternative view:** Permit Edit on UnderReview with reviewer banner.
- **Seeking input on:** Is the stricter gate acceptable, or do reviewers want UnderReview Edits?

## Naming Decisions

| Item | Name | Context |
|------|------|---------|
| Route | `/Application/{appId}/Item/{itemId}/Quotation/{quotationId}/Edit` | Matches existing nested route pattern (`Convert`, `Replace`, `Delete`). |
| Shared partial | `_QuotationFieldsForm.cshtml` (illustrative) | Extracted block reused by Supplier/Add and Quotation/Edit. Plan to confirm final name. |
| Spec dir | `023-quotation-edit` | Sequential, follows project numbering convention. |

## Open Questions

- [ ] OQ-1: Include "Replace file" affordance on the Edit page? Default: no (keep on Application/Edit row).
- [ ] OQ-2: Emit `AdminAuditEvent` for applicant-initiated quote edits? Default: silent (matches Item/Edit).
- [ ] OQ-3: Deep-link the returned-for-changes email CTA to the Quotation Edit URL? Defer to spec 021 email-template touch-up.

## Risk Areas

| Risk | Impact | Mitigation |
|------|--------|------------|
| Supplier/Add E2E regression from partial extraction | Med | SC-005 explicitly asserts existing suite stays green; partial is behavior-preserving extraction. |
| AI cache stale-read window for reviewer | Low | Reviewer regenerates on demand; documented as accepted trade-off in D-2. |
| Concurrent two-tab edits silently overwrite | Low | Single-actor scope; matches Item/Edit precedent. R-1 plan-phase ruling. |
| `LegacyNeedsReview`-flagged quotes mis-targeted by Edit | Low | FR-011 explicitly hides affordance + rejects POST; admin path per spec 015 is unchanged. |
| Currency change with no published rate at save time | Low | FR-005 + existing `UserFacingErrorCode.MissingExchangeRate` translator; rolls back atomically. |

---
*Share with reviewers before implementation.*
