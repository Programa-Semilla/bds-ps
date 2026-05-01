# Review Brief: Centralized Supplier Catalog (013-supplier-catalog)

**Spec:** specs/013-supplier-catalog/spec.md
**Generated:** 2026-04-30

> Reviewer's guide to scope and key decisions. See full spec for details.

---

## Feature Overview

Today the platform stores supplier data as a single flat row per legal ID and asks applicants to type both contact information and CCSS / Hacienda / SICOP compliance flags by hand on every quotation. Compliance values typed by applicants feed the recommendation algorithm directly — a trust model nobody designed for.

This feature converts suppliers into a structured, admin-curated catalog. One canonical `Supplier` per legal ID, N `SupplierBranches` underneath (one office/contact each), and the four admin-controlled flags (CCSS, Hacienda, SICOP, electronic-invoice) move off applicant forms entirely. Applicant-entered records flow through `Draft → PendingReview → Verified | Rejected`. Migrated suppliers are marked `Verified` to preserve recommendation parity day-one.

## Scope Boundaries

- **In scope:** new `SupplierBranches` table, supplier `VerificationStatus` lifecycle, applicant search-by-legal-ID + branch picker UX, admin Suppliers page (list, filter, edit, verify, reject), `SupplierScore` signature change to carry branch context plus verification-state flags, single-transaction forward-only migration with assertion checks.
- **Out of scope:** external CCSS / Hacienda / SICOP API integration, supplier portal/login, supplier merge/dedup tooling, bulk import, audit log table, branch soft-delete UI, applicant-facing notifications on supplier verification, direct admin supplier creation.
- **Why these boundaries:** the seed asks for structural reuse and admin-controlled compliance, not a full supplier-management product. External integrations and portals are 3-month projects each. Applicant notifications can be a follow-up wow-moment per spec 011 patterns.

## Critical Decisions

### Branch entity, not flat contacts
- **Choice:** new `SupplierBranches` table; `Quotations` FKs to a branch (plus a denormalized `SupplierId`).
- **Trade-off:** more rows and one extra FK on every quotation, in exchange for modeling reality (offices have their own contacts and addresses). Quotations.SupplierId is kept denormalized for fast joins.
- **Feedback:** is the branch model worth the extra entity, or should we ship a `SupplierContacts` flat-list first and add branches later?

### Admin owns the four "is-compliant" booleans, including e-invoice
- **Choice:** `IsCompliantCCSS`, `IsCompliantHacienda`, `IsCompliantSICOP`, AND `HasElectronicInvoice` are admin-only. Removed from every applicant form.
- **Trade-off:** strictest data hygiene; admin must touch every Verified supplier at least once to set e-invoice. We previously left e-invoice as applicant-trusted; this spec consolidates all four as admin-verified for consistency.
- **Feedback:** is e-invoice really worth the admin overhead, or is it self-evident enough to leave as applicant-trusted?

### `PendingReview` is creator-only
- **Choice:** an applicant who created a Draft supplier sees it after submission flips it to `PendingReview` (read-only), but other applicants cannot find it via legal-ID lookup until admin promotes to `Verified`.
- **Trade-off:** stricter than initial draft of the spec. Slows cross-applicant reuse — same legal ID typed by applicant B while A's Pending supplier sits unverified will create a `Suppliers.LegalId` UNIQUE collision and the system has to redirect B to A's record (or wait). Important: the UNIQUE collision case is documented in Edge Cases but the recovery UX is generic ("re-poll, find existing supplier") and assumes both applicants will eventually see the same record after Verified — the spec does not specify what B sees when the same legal ID is being held by A's PendingReview.
- **Feedback:** revisit this if usage shows admin queue lag becomes a UX bottleneck.

### Submission allowed with Draft / PendingReview suppliers
- **Choice:** `Application.Submit` flips owned Drafts to PendingReview atomically, but does NOT block on admin verification. Reviewer can score in parallel; pending suppliers contribute zero compliance points.
- **Trade-off:** loosest coupling — admin queue lag never blocks applicants. Risk: reviewers see a misleading low score on a supplier the admin will eventually mark fully compliant. Mitigated by a "Pending verification" badge.
- **Feedback:** is the badge enough, or do we need a "this score will change after verification" tooltip explicitly?

### Migration trusts existing applicant-set compliance
- **Choice:** every existing supplier becomes `Verified` with its current compliance flags preserved. `VerifiedByUserId` = system admin sentinel from spec 009.
- **Trade-off:** zero recommendation regression on day-one (SC-003 promises byte-for-byte parity), at the cost of trusting historical applicant input. Only fix if wrong is an admin sweep.
- **Feedback:** acceptable risk, or do we need an admin-driven re-verification queue post-launch?

## Areas of Potential Disagreement

### Should the spec also require notification when admin verifies/rejects a Pending supplier?
- **Decision:** notifications are out of scope.
- **Why this might be controversial:** an applicant whose application is in review with a PendingReview supplier has no signal when the score changes. Reviewers may grade them low silently.
- **Alternative view:** ship a small in-app notification (reusing spec 011 wow-moment patterns) so the applicant sees "your supplier was verified" or "your supplier was rejected" on their dashboard.
- **Seeking input on:** is the lack of applicant feedback acceptable for v1?

### Should "Sede principal" (default branch name on migration) be Spanish-only?
- **Decision:** yes, hardcoded Spanish per the es-CR localization scope (spec 012).
- **Why this might be controversial:** if the platform ever localizes to another language, the migrated rows are stuck.
- **Alternative view:** use a localization key + fall-through to "Sede principal" in es-CR.
- **Seeking input on:** does v1's Spanish-only product position justify the hardcoded string?

### Should reviewers be allowed to pick a Rejected supplier as the chosen quotation?
- **Decision:** yes — they can pick it, but the supplier cannot bear the "Recommended" badge regardless of score.
- **Why this might be controversial:** allowing the reviewer to award funds to a Rejected supplier feels wrong.
- **Alternative view:** outright disable choice on quotations whose supplier is Rejected.
- **Seeking input on:** is an inline banner enough, or do we need a hard block?

## Naming Decisions

| Item | Name | Context |
|---|---|---|
| New table | `SupplierBranches` | sibling to `Suppliers`; carries office/contact data |
| Status enum on supplier | `VerificationStatus` (Draft, PendingReview, Verified, Rejected) | `tinyint` column |
| Default migrated branch label | `Sede principal` | Spanish, matches es-CR localization scope |
| Score result flags | `IsSupplierVerified`, `IsSupplierRejected` | added to `SupplierScore` record |
| Aggregate-root lifecycle methods | `SubmitForReview`, `Verify`, `Reject` | on `Supplier` per Constitution Principle II |

## Open Questions

- [ ] **Q1**: If an Application is deleted while it has a Draft-status supplier with no other quotation references, do we cascade-delete the supplier? Spec assumes yes; needs confirmation.
- [ ] **Q2**: Should reviewers be hard-blocked from picking a Rejected supplier, or only soft-discouraged via banner? Spec assumes soft-discouraged.
- [ ] **Q3**: PendingReview suppliers when their parent application is sent back to draft by reviewer — do they revert to `Draft`? Spec assumes no (admin retains control); still flagged for confirmation.

## Risk Areas

| Risk | Impact | Mitigation |
|---|---|---|
| Migration data loss / inconsistency | High | Single forward-only transaction, assertion checks, full prod backup, staging dry run |
| Recommendation regression on day-one | High | Migrated suppliers are `Verified` with existing flags preserved; SC-003 is a byte-for-byte parity test |
| Admin queue grows unattended | Medium | Filtering UX (FR-031); a queue-count dashboard badge can be a follow-up |
| Score divergence over time as admin edits compliance mid-review | Medium | Scores are computed live per spec 003 contract; called out explicitly as expected behavior |
| Applicant blocked when their typed legal ID matches another applicant's PendingReview | Medium | Edge case described; UNIQUE-collision path needs explicit UX in implementation phase |
| Forgotten i18n on hardcoded "Sede principal" if product expands beyond es-CR | Low | Acceptable for v1 per spec 012 scope |

---
*Share with reviewers before implementation.*
