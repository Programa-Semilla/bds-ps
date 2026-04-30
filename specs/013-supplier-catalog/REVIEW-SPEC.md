# Spec Review: Centralized Supplier Catalog with Multi-Branch Support and Admin-Controlled Compliance

**Spec:** `specs/013-supplier-catalog/spec.md`
**Date:** 2026-04-30
**Reviewer:** Claude (`speckit-spex-gates-review-spec`)

## Overall Assessment

**Status:** SOUND — APPROVED (iteration 2)

**Summary:** The spec is well-structured, complete, and implementable. Iteration-1 flagged two Important ambiguities (e-invoice trust model, PendingReview cross-applicant visibility); both are now resolved in the spec. User stories are independently testable, requirements are numbered and specific, success criteria are measurable.

## Iteration 2 — Resolutions Applied

- **E-invoice trust model: admin-verified.** FR-020 no longer asks the applicant for `HasElectronicInvoice`; FR-021 initializes it to `false` on Draft creation; FR-022 explicitly bars applicant edits to it; FR-040 names e-invoice as one of the four admin-only flags. FR-051's "four compliance/e-invoice factors zero for pending" is now consistent. User Story 1 acceptance scenario 1 updated to render e-invoice read-only on the existing-supplier flow. User Story 3 narrative updated.
- **PendingReview visibility: creator-only.** FR-002 split into two clauses — `Verified` is visible to everyone, `PendingReview` is visible only to its creator. FR-003 now explicitly states cross-applicant `PendingReview` is invisible to lookup. FR-010 narrowed to "existing `Verified`, or creator's `PendingReview`". The permission matrix in FR-070 was rebuilt with separate columns for owner vs. other-applicant PendingReview/Rejected access; non-creators see "not visible (lookup miss)".

Iteration-1 Optional item also applied: SC-005 reworded to "produces identical scoring totals" (no more `±0%` contradiction).

## Completeness: 5/5

### Structure
- All required sections present (User Scenarios & Testing, Edge Cases, Functional Requirements, Key Entities, Success Criteria, Assumptions).
- Recommended sections included (Out-of-scope is folded into Assumptions, which is acceptable).
- No placeholder text, no `[NEEDS CLARIFICATION]` markers, no TBDs.

### Coverage
- Seven user stories cover applicant happy path, branch addition, draft creation, submission lifecycle, admin verification, admin correction, and admin queue UX.
- Edge cases address concurrency, abandoned drafts, normalization, rejected-supplier handling, mid-review rejection, single-branch UX collapse, reviewer read-only enforcement, re-verification.
- Functional requirements are grouped by capability (identification, existing-supplier flow, new-supplier flow, admin control, compliance/lifecycle, submission/reviewer signals, migration, permissions). 7 FR groups, 30+ requirements total.
- Migration is explicitly specified (FR-060..063).
- Key Entities section names every aggregate touched.

**Issues:** None.

## Clarity: 4/5

### Language Quality
- Requirements use MUST consistently. No "should" weakening.
- No `etc.`, no "user-friendly", no "fast/slow".
- Specific status names (`Draft`, `PendingReview`, `Verified`, `Rejected`) used consistently throughout.

**Ambiguities Found:**

1. **FR-051 lumps electronic-invoice into "compliance/e-invoice factors" for pending suppliers**, but FR-020 lists `HasElectronicInvoice` as a field the applicant fills on the new-supplier form. This creates an inconsistency: is `HasElectronicInvoice` applicant-trusted (and thus always scoring) or admin-verified (and thus zero-until-Verified)? The current wording implies the latter, which contradicts the form design. **Suggestion:** Decide explicitly. If e-invoice stays applicant-trusted, change FR-051 to "...zero points to the three compliance factors (CCSS, Hacienda, SICOP)..." and clarify that the e-invoice point is awarded based on the applicant-supplied flag regardless of `VerificationStatus`. If e-invoice should be admin-verified, remove it from the applicant form in FR-020 and document the change in FR-033's editable fields.

2. **FR-002 + FR-070 expose `PendingReview` suppliers to other applicants via lookup**. FR-002 says lookup returns supplier metadata + branches when status is `Verified` OR `PendingReview`. The permission matrix (FR-070) confirms this for "Applicant (other)" with "read (via lookup of Verified/Pending only)". This means an applicant on Application X can reuse a `PendingReview` supplier created by another applicant on Application Y before admin has vetted it. This is an important product decision (accelerates reuse, but spreads not-yet-vetted data). **Suggestion:** Add a one-line rationale in the spec explaining the choice, OR change the rule to "lookup returns only Verified suppliers to other applicants; PendingReview is visible only to its creator." User stories 1, 2, 5 do not exercise this case, so the test suite would not catch a regression.

3. **SC-005 phrasing "within ±0% of its current scoring math"** is technically a contradiction (±0% means exact equality). Readable but pedantically unsound. **Suggestion:** "produces identical scoring totals to the current implementation for verified suppliers".

## Implementability: 5/5

### Plan Generation
- Domain model is fully specified (Supplier aggregate root with Branches, lifecycle enum, transition methods named in Assumptions).
- Schema changes are explicit (which columns drop, which add, which migrate to where).
- Migration is a single ordered transaction with assertion checks — directly translatable to a dacpac post-deploy script (matches Constitution Principle IV).
- Dependencies on prior specs are named: spec 003 (`SupplierScore`), spec 009 (admin sentinel + admin shell), spec 011 (typography reuse), spec 012 (es-CR localization).
- Scope is appropriately bounded by Out-of-Scope items in Assumptions.

**Issues:** None.

## Testability: 5/5

### Verification
- Each user story has an Independent Test paragraph that is operational (seed → act → assert).
- Acceptance scenarios are written in Given/When/Then form, suitable for direct Playwright translation.
- Success criteria are measurable: SC-001 (≥70% field reduction), SC-002 (zero compliance checkboxes), SC-003 (byte-for-byte recommendation parity), SC-004 (queue age ≤ 1 business day), SC-005 (scoring math parity), SC-006 (migration < 60s).
- SC-001 baseline ("today") is implicit but acceptable — the current `AddSupplierViewModel` is the comparison anchor.
- SC-004 requires production telemetry to verify; flag as acceptable ("measured in production via queue age" is explicit).

**Issues:** None blocking. SC-005 wording per Clarity finding 3.

## Constitution Alignment

Constitution v1.0.0 was reviewed; the spec aligns on all six principles:

- **I. Clean Architecture**: Spec specifies WHAT (entities, use cases, lifecycle) without dictating layer placement; the plan phase will allocate to Domain/Application/Infrastructure/Web.
- **II. Rich Domain Model**: Assumptions explicitly call out `Supplier` aggregate root with lifecycle methods (`SubmitForReview`, `Verify`, `Reject`, branch CRUD). State transitions in FR-024, FR-035 are gated by entity behavior. ✓
- **III. End-to-End Testing**: Each P1/P2 user story is independently testable and includes an "Independent Test" paragraph that maps cleanly to a Playwright spec. ✓
- **IV. Schema-First Database Management**: Migration in FR-060..063 is described as a single forward-only transaction with assertion checks — implementable as a dacpac pre/post-deploy script (no EF migrations). The spec does NOT mention EF migrations or `EnsureCreated`. ✓
- **V. Specification-Driven Development**: This very document is the artifact. ✓
- **VI. Simplicity and Progressive Complexity**: Out-of-scope list is generous (no external APIs, no merge tooling, no bulk import, no audit log table, no notifications, no admin direct-create, no soft delete, no province enum, no supplier portal). YAGNI applied. ✓

**Violations:** None.

## Cross-Artifact Consistency

`plan.md` and `tasks.md` do not yet exist for this feature, so `/speckit-analyze` is not applicable. Re-run after `/speckit-plan`.

## Recommendations

### Critical (Must Fix Before Implementation)

None. The spec is implementable as written.

### Important (Should Fix)

- [x] **E-invoice trust model resolved** — admin-verified. FR-020/FR-021/FR-022/FR-040 updated. ✓
- [x] **PendingReview cross-applicant visibility resolved** — creator-only. FR-002/FR-003/FR-010/FR-070 updated. ✓

### Optional (Nice to Have)

- [x] **SC-005 reworded** to "produces identical scoring totals". ✓
- [ ] **Note the SC-004 baseline window** ("admin's working day in 90% of cases over a rolling 30-day window") for measurability — deferred; not blocking.

## Conclusion

Spec is approved (iteration 2). All Critical, Important, and Optional items either fixed or explicitly deferred as non-blocking.

**Ready for implementation:** Yes.

**Next steps:**
1. Run `/speckit-plan` to generate `plan.md`.
2. Re-run cross-artifact analysis (`/speckit-analyze`) once `plan.md` and `tasks.md` exist.
