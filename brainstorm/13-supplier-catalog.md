# Brainstorm: Centralized Supplier Catalog with Multi-Branch Support

**Date:** 2026-04-30
**Status:** spec-created
**Spec:** specs/013-supplier-catalog/

## Problem Framing

The platform currently stores suppliers as a single flat row per legal ID (`Suppliers` table) where the applicant types contact info AND marks CCSS / Hacienda / SICOP compliance flags themselves on every quotation. Reuse-by-legal-ID happens at the data layer (`ISupplierRepository.GetByLegalIdAsync`), but the UX never surfaces it: applicants always see a blank form. Worse, applicant-supplied compliance values feed `SupplierScore` (spec 003) directly — the recommendation algorithm trusts data the applicant invented thirty seconds ago.

The seed (Spanish, in `brainstorm/seeds/suppliers-db-md`) asks for two things: structure (one canonical supplier per legal ID with multiple offices/branches underneath) and trust (compliance fields owned by admins, not applicants). Submitted-but-unverified supplier data needs a lifecycle: applicants enter drafts, submission flips them to "pending admin review", admin verifies or rejects.

## Approaches Considered

### A: Branch entity + admin-owned compliance + status enum (Selected)

- New `SupplierBranches` table (1 supplier → N branches). Quotations FK to a branch.
- `VerificationStatus` enum on `Suppliers`: `Draft` / `PendingReview` / `Verified` / `Rejected`.
- All four "is-compliant" flags (CCSS, Hacienda, SICOP, electronic-invoice) move to admin-only.
- Manual admin checkboxes — no external CCSS / Hacienda / SICOP API integration.
- Migration marks every existing supplier as `Verified` with current compliance preserved; one default `Sede principal` branch carries existing contact data; quotations re-point to that branch.
- Pros: matches the seed verbatim, models reality, zero recommendation regression on day-one (existing flags preserved), no external API dependencies.
- Cons: largest schema change since spec 010. Adds one entity and one FK to every quotation.

### B: Flat contacts (no branch entity) + admin compliance + status enum

- Keep `Suppliers` flat per legal ID. Add `SupplierContacts` child for contact people only (no per-office address).
- Same admin-owned compliance + status enum as A.
- Pros: smaller schema delta. Simpler queries.
- Cons: cannot model real branches/offices with their own province/address — exactly what the seed asks for. Hacks around the requirement.

### C: Status enum only — no branches, no admin/applicant separation

- Add `VerificationStatus` enum to flatten current shape, but keep applicants entering compliance.
- Pros: smallest possible change.
- Cons: leaves the "applicant lies about compliance" problem completely untouched. Misses the seed's central ask.

## Decision

Selected **Approach A: Branch entity + admin-owned compliance + status enum**. Best fit to the seed's two stated requirements (structure + trust). The schema delta is bigger than B, but the algorithm change is tiny because `SupplierScore` (spec 003) already reads compliance from `Supplier`, not `Quotation` — only the signature changes (now passes branch context for reviewer UI display).

Key design decisions captured in the spec:

- **Branch entity:** `SupplierBranches` table with `(SupplierId, IsDefault)` filtered unique index to enforce exactly one default per supplier.
- **Compliance ownership:** all four `is-compliant` flags (CCSS, Hacienda, SICOP, electronic-invoice) are admin-only. Removed from applicant forms entirely after iteration-2 review tightened the e-invoice trust model.
- **Status lifecycle:** `Draft` (applicant-owned, hidden from others) → `PendingReview` (creator-only visibility, locked from applicant edits, surfaces in admin queue) → `Verified` (admin-trusted, visible to all applicants) | `Rejected` (admin-blocked with reason, lookup miss for new quotations).
- **Submission policy:** allowed even with Draft / PendingReview suppliers; pending = 0 compliance points; Rejected = no Recommended badge.
- **Cross-applicant `PendingReview` visibility:** creator-only (decided in iteration-2 review). Other applicants searching the same legal ID get a lookup miss until admin promotes to Verified.
- **Migration:** forward-only, single transaction. Existing suppliers → `Verified` (system-admin sentinel from spec 009 as VerifiedByUserId). Default branch `Sede principal` carries existing contact data. Quotations re-pointed via JOIN. Assertion checks abort on inconsistency.
- **Algorithm impact:** `SupplierScore.ComputeForItem` signature changes from `(Quotation, Supplier)` pairs to `(Quotation, Supplier, SupplierBranch)` triples. Math unchanged. Result record gains `IsSupplierVerified` / `IsSupplierRejected` flags. `IsRecommended = Total == maxScore && !IsSupplierRejected`.

## Spec Review Trail

- Iteration 1: SOUND — flagged two Important items (e-invoice trust model ambiguity between FR-020/FR-051; PendingReview cross-applicant visibility).
- Iteration 2: APPROVED — both Important items resolved (admin-verified e-invoice; creator-only PendingReview). SC-005 wording fixed.

## Open Threads

- **Q1**: Cascade-delete a Draft supplier when its parent application is deleted? Spec assumes yes; confirmation deferred to plan phase.
- **Q2**: Hard-block reviewer from picking a Rejected supplier, or only soft-discourage via banner? Spec assumes soft.
- **Q3**: When reviewer sends application back to draft, do PendingReview suppliers revert to Draft? Spec assumes no (admin retains control).
- **Q4**: Applicant notification when their draft supplier is verified or rejected — out of v1 scope; potential follow-up wow-moment per spec 011 patterns.
- **Q5**: Admin queue count badge on the admin dashboard — out of v1 scope; cheap follow-up if queue lag becomes a UX bottleneck.
