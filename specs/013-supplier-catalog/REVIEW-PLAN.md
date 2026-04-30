# Review Guide: Centralized Supplier Catalog with Multi-Branch Support and Admin-Controlled Compliance

**Spec:** [spec.md](spec.md) | **Plan:** [plan.md](plan.md) | **Tasks:** [tasks.md](tasks.md)
**Generated:** 2026-04-30

---

## What This Spec Does

Today, suppliers exist in this platform as a single flat row per legal ID, where applicants type contact data and self-mark CCSS / Hacienda / SICOP compliance flags every time they add a quotation. The recommendation algorithm trusts those self-marked compliance values directly. This spec turns suppliers into a structured, admin-curated catalog: one canonical `Supplier` per legal ID with N `SupplierBranches` underneath, four admin-only flags (CCSS / Hacienda / SICOP / electronic-invoice), and a `Draft → PendingReview → Verified | Rejected` lifecycle on the supplier identity. Migration marks every existing supplier as `Verified` so existing applications keep their current recommendations byte-for-byte on day one.

**In scope:** new `SupplierBranches` table, supplier verification lifecycle, applicant search-by-legal-ID + branch picker UX, admin Suppliers page (list, filter, edit, verify, reject), `SupplierScore` signature change to carry branch context plus verification-state flags, single-transaction forward-only migration with assertion checks.

**Out of scope:** external CCSS / Hacienda / SICOP API integration; supplier portal/login; supplier merge/dedup tooling; bulk import; audit log table; branch soft-delete UI; applicant notifications on verify/reject; direct admin supplier creation; province enum (free text in v1).

## Bigger Picture

This is the 13th feature in this codebase and the first to materially reshape an existing aggregate root since spec 003 (the supplier evaluation engine that introduced `SupplierScore`). It depends on three earlier specs in non-trivial ways: spec 003 owns the recommendation math the migration must preserve byte-for-byte ([SC-003](spec.md#measurable-outcomes)); spec 009 introduced the system-admin sentinel user that the migration uses as `VerifiedByUserId` for migrated rows ([FR-063](spec.md#migration-of-existing-data)); spec 012 owns the es-CR localization conventions every new applicant- and admin-facing string follows.

The structural reshape is the kind of change that's tempting to extend into adjacent concerns — applicant notifications, admin audit trail, supplier portal. The spec is unusually disciplined about what it leaves out (see the long out-of-scope list in [Assumptions](spec.md#assumptions)). Worth confirming the appetite for this discipline matches yours: it's much easier to add a notification or audit log later as a focused follow-up than to discover mid-implementation that the migration semantics need to accommodate one of those concerns.

The dacpac migration approach ([research R3](research.md#r3--migration-mechanics-under-dacpac-constitution-iv)) deliberately copies the spec 010 currency-rollout pattern: legacy columns survive one release with a `TODO[013-cleanup]` marker, then ship the column drop in a follow-up PR. That's the established team pattern but it does mean the schema carries duplicated supplier-contact data for one release window.

---

## Spec Review Guide (30 minutes)

> This guide focuses your 30 minutes on the parts that need human judgment.

### Understanding the approach (8 min)

Read [User Story 1](spec.md#user-story-1---reuse-a-verified-supplier-with-an-existing-branch-priority-p1) and [User Story 4](spec.md#user-story-4---application-submission-locks-draft-suppliers-and-routes-to-admin-priority-p1) for the core flow, then [Functional Requirements: Supplier identification](spec.md#supplier-identification) and [Submission and lifecycle](spec.md#submission-and-lifecycle) for the lifecycle promise. As you read, consider:

- Does the `Draft → PendingReview → Verified | Rejected` lifecycle actually match what your operations team does today, or is it modeling something cleaner than reality?
- Is "submission allowed even when every supplier is Draft/PendingReview" ([FR-050](spec.md#submission-blocking-and-reviewer-signals)) the right loose-coupling, or do you want submission gated on at least one Verified supplier?
- The "Pendiente de verificación" badge on the reviewer's quotation row ([FR-051](spec.md#submission-blocking-and-reviewer-signals)) is the only signal a reviewer gets that a low score is provisional. Is a badge enough, or should the score row also carry a "this score will change after verification" tooltip?

### Key decisions that need your eyes (12 min)

**E-invoice flag is admin-only**, alongside the three regulatory compliance flags ([FR-040](spec.md#compliance-and-lifecycle), iteration-2 fix in [REVIEW-SPEC.md](REVIEW-SPEC.md#iteration-2--resolutions-applied)).
The original draft kept `HasElectronicInvoice` on the applicant form (it's not regulatory like CCSS / Hacienda / SICOP) but the iteration-2 review consolidated all four flags as admin-verified for consistency.
- Reviewer question: is the data-hygiene gain worth forcing admin to touch every Verified supplier at least once to set e-invoice? Could you imagine a future state where applicants self-declare e-invoice and admin only validates the three regulatory ones?

**`PendingReview` lookup is creator-only** ([FR-002](spec.md#supplier-identification), [FR-070 permission matrix](spec.md#permissions)).
A supplier created by Applicant A and waiting for admin verification is invisible to Applicant B who happens to type the same legal ID. Applicant B will hit the new-supplier form, fill it out, and try to save — at which point the unique-constraint serialization recovery ([research R4](research.md#r4--concurrent-insert-recovery-when-two-applicants-type-the-same-new-legal-id)) catches them.
- Reviewer question: is the operational cost of "two applicants in the same week create the same legal ID" rare enough that this UX is acceptable, or does it warrant making `PendingReview` cross-applicant-visible?

**Quotation uniqueness rule unchanged** ([research R1](research.md#r1--quotation-uniqueness-constraint-under-the-branch-model)).
The existing rule "one quotation per (item, supplier)" is preserved, NOT relaxed to "one per (item, branch)". Branch is contact metadata, not a separate quote source.
- Reviewer question: is the procurement-source view ("one supplier = one quote") right? An applicant who legitimately gets two quotes from two branches of the same supplier (e.g., a manufacturer's two warehouses with different lead times) cannot submit both today. Edge case, but worth confirming.

**Migration trusts existing applicant-set compliance flags** ([FR-060..062](spec.md#migration-of-existing-data)).
Every existing supplier becomes `Verified` with its current applicant-set CCSS / Hacienda / SICOP / e-invoice flags preserved. SC-003 promises byte-for-byte recommendation parity day-one.
- Reviewer question: is "trust historical applicant input" acceptable, or should the migration mark all existing rows as `PendingReview` and force an admin sweep before any new application is processed? The current choice optimizes for zero day-one regression at the cost of some unknown number of historically-incorrect compliance flags persisting until an admin happens to edit them.

**Legacy columns survive one release** ([research R3](research.md#r3--migration-mechanics-under-dacpac-constitution-iv), [T090](tasks.md#phase-10-polish--cross-cutting-concerns)).
Following the spec 010 currency-rollout pattern, `Suppliers.{ContactName, Email, Phone, Location, ShippingDetails, WarrantyInfo}` stay declared in the dacpac for one release with a `TODO[013-cleanup]` marker. T090 is the follow-up.
- Reviewer question: is the one-release window the right cleanup cadence? In smaller deploys we could drop them in the same PR via a two-step deploy; in larger deploys we'd want telemetry to confirm the migration ran cleanly first. Which world is this?

### Areas where I'm less certain (5 min)

- [FR-025](spec.md#new-supplier-flow): "PendingReview suppliers do NOT revert to Draft when the parent application is sent back to draft". The spec assumes admin retains control regardless. I am not 100% sure that's the desired UX — if a reviewer sends an application back specifically because the supplier data was wrong, the applicant may now be unable to fix it without admin involvement, which feels like friction. Worth confirming during your review of [User Story 4](spec.md#user-story-4---application-submission-locks-draft-suppliers-and-routes-to-admin-priority-p1).
- [FR-070 permission matrix row "Reviewer"](spec.md#permissions): the row says "read" on every column, but the column "Owned PendingReview / Rejected supplier" is owned by the *applicant who is being reviewed*, not the reviewer. The intent is "reviewer can see whatever supplier the application they're reviewing references". I think the matrix conveys that, but the row labeling could read as "reviewers can look up arbitrary suppliers", which they can't via the supplier lookup endpoint (only admins can). Worth a second pair of eyes on the matrix.
- The applicant-facing Add-quotation form is being rewritten as a step-flow ([T039](tasks.md#phase-3-user-story-1--reuse-a-verified-supplier-with-an-existing-branch-priority-p1-mvp)). I planned a tiny vanilla-JS hook for the 250ms debounce, but every other JS-heavy surface in this codebase uses `PlatformMotion` from spec 011. If `PlatformMotion.debounce` exists, the right thing is to reuse it; if not, I added a new helper. Worth confirming during the front-end task.

### Risks and open questions (5 min)

- If a deploy hits a database where the spec 009 system-admin sentinel is missing, the migration aborts loudly via [FR-063](spec.md#migration-of-existing-data) / [T007](tasks.md#schema-dacpac) `THROW 50010`. Is that "abort and rollback" the right ops behavior, or do you want a warning-and-continue path? My read is "abort is correct, sentinel-missing is a deploy-pipeline misconfiguration", but I haven't talked to the ops team.
- The migration parity test ([T028](tasks.md#migration-parity-test-sc-003)) seeds the OLD schema state via raw SQL, runs the migration, and asserts byte-for-byte score parity. It does NOT test the dacpac deploy engine itself; if the deploy engine reorders the post-deploy script vs. the column-drop step, the test won't catch it. Production parity ultimately rests on the staging dry-run. Acceptable?
- [SC-006 "migration runs in under 60s"](spec.md#measurable-outcomes) currently has no automated assertion. T028 has the parity check but not a timing one. Should T028 also assert a wall-clock budget, or is operational measurement enough?
- The applicant who creates a Draft supplier and walks away leaves a `Draft` row that no other applicant can see. The spec assumes cascade-delete-on-application-delete (open thread Q1), but the current code's application-delete cascade behavior should be re-checked during implementation. Could leak orphan suppliers if the assumption is wrong.

---
*Full context in linked [spec](spec.md), [plan](plan.md), and [tasks](tasks.md).*
