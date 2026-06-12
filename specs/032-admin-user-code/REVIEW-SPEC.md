# Spec Review: Admin-only user provisioning + unique applicant User Code

**Spec:** specs/032-admin-user-code/spec.md
**Date:** 2026-06-11
**Reviewer:** Claude (speckit-spex-gates-review-spec)

## Overall Assessment

**Status:** SOUND

**Summary:** The spec is complete, unambiguous, and implementable. All three user stories are independently testable with concrete acceptance scenarios, every functional requirement maps to a verifiable outcome, and the four design decisions that could have caused drift (new field vs. reuse, required-for-Solicitante, filter scope, registration-404) were resolved before authoring. Ready for planning.

## Completeness: 5/5

### Structure
- Purpose (via Input + Story "Why" sections), Functional Requirements, Success Criteria, Edge Cases, Assumptions, Out of Scope, Dependencies, Key Entities all present.
- No placeholder/TBD text remains; template scaffolding fully replaced.

### Coverage
- Registration removal, User Code lifecycle (create/edit/uniqueness/required/visibility), and the three search surfaces are each covered by dedicated FRs and acceptance scenarios.
- Error/edge handling explicit: blank/whitespace code, duplicate code, legacy applicants without code, role-changed-away, empty search term, replayed POST to the dead endpoint.

**Issues:** None.

## Clarity: 5/5

### Language Quality
- Requirements use MUST/MUST NOT consistently; the single SHOULD (FR-016, surfacing the column) is intentionally discretionary and bounded ("where it adds value and fits the layout").
- Quantified where it matters: ≤50 chars, 404, "case-insensitive and accent-insensitive", "empty search term preserves today's behavior".

**Ambiguities Found:** None blocking. The only soft term ("where it adds operational value", FR-016) is deliberately a nice-to-have, not a gate.

## Implementability: 5/5

### Plan Generation
- A plan can be generated directly: the spec names the affected surfaces conceptually (admin users list, reviewer queue + row refresh, admin reports + applicants CSV) and the data home (applicant record, beside identification).
- Dependencies are concrete and already in the codebase (role wiring, identification capture, group/fund selection, existing search controls).
- Scope is bounded and modest; uniqueness-with-nullable and "required only for one role" are well-understood patterns the codebase already applies to LegalId.

**Issues:** None. Note for planning: storage-level uniqueness over a nullable column and the es-CR duplicate-message path are the two spots warranting explicit task coverage (the duplicate path is typically E2E-only since the in-memory test provider does not enforce unique indexes — consistent with spec 030's handling of `UX_Processes_Name`).

## Testability: 5/5

### Verification
- Every Success Criterion is observable from the browser/HTTP layer (404, blocked save, search returns/excludes a seeded applicant, field absence for non-applicants, read-only profile value).
- SC-006 explicitly ties delivery to filtered E2E green for the touched areas, matching Constitution Principle III and the project delivery bar.

**Issues:** None.

## Constitution Alignment

- **I. Clean Architecture** — spec stays at WHAT level; no layer violations implied.
- **II. Rich Domain Model** — User Code required/unique rules are entity invariants (applicant), consistent with the existing LegalId pattern.
- **III. E2E (non-negotiable)** — SC-006 and each story's Independent Test demand E2E coverage.
- **IV. Schema-First** — adding the column belongs in the dacpac; spec correctly avoids mandating EF migrations (it speaks of "storage level" only).
- **V. SDD** — prioritized, independently testable user stories present.
- **VI. Simplicity / YAGNI** — format/pattern validation, backfill, and bulk import explicitly deferred to Out of Scope.

**Violations:** None.

## Recommendations

### Critical (Must Fix Before Implementation)
- None.

### Important (Should Fix)
- None.

### Optional (Nice to Have)
- [ ] During planning, decide explicitly whether the reviewer-queue search gains a visible User Code column or only matches on it (FR-016 leaves this open by design); keep it minimal per the spec.

## Conclusion

The specification is sound and implementable as written. The deferred items are genuinely out of scope, and the one discretionary requirement is clearly marked.

**Ready for implementation:** Yes (after planning)

**Next steps:** Proceed to `/speckit-plan` to produce the technical design, dacpac column + filtered unique index decision, and the per-surface search-widening task breakdown.
