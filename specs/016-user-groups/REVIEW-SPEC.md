# Spec Review: Group-Scoped Reviewer Access

**Spec:** specs/016-user-groups/spec.md
**Date:** 2026-05-07
**Reviewer:** Claude (speckit-spex-gates-review-spec)

## Overall Assessment

**Status:** SOUND

**Summary:** Spec is complete, clear, and implementable. One important note for the implementation plan (concurrency policy on membership writes) and two optional refinements. Nothing blocks planning.

## Completeness: 5/5

### Structure
- All required sections present: Purpose (in lead paragraph + US1), Functional Requirements (16), Success Criteria (6), Error Handling (covered in Edge Cases + ERROR HANDLING-style content embedded in FRs), Non-Functional Requirements (5), Edge Cases (9), Dependencies, Out of Scope, Assumptions, Open Questions.
- No TBD or placeholder text.
- User stories are prioritized (P1, P1, P1, P2) and each has an Independent Test description.

### Coverage
- Three reviewer-facing surfaces (queue, signing inbox, search) plus the detail page have explicit FRs.
- Admin bypass is explicitly enumerated (FR-015).
- Cascade-delete behavior, including the zero-group fallout, has its own user story (US4) and is exercised by acceptance scenarios and SC-004.
- Applicant own-access carve-out is explicit (FR-016, US3.3).

**Issues:** none.

## Clarity: 5/5

### Language Quality
- Normative requirements use MUST / MUST NOT / MAY consistently.
- No "should", "might", "fast", "user-friendly", "etc.", or other red-flag terms in normative text.
- Comparison rules are concrete ("case-insensitive", "intersect at least one group", "403", "next request").

**Ambiguities Found:** none material.

## Implementability: 4/5

### Plan Generation
- The Key Entities + Dependencies sections name existing anchors (`ApplicationUser`, `Applicant`, `Application`, `AdminUsersController`, `UserAdministrationService`, `AdminImpliesReviewerClaimsTransformation`) so the plan team has clear extension points.
- Approach is implied (groups on `ApplicationUser`, applicants inherit via the `UserId` link) without dictating implementation; that is appropriate for a spec.
- Scope is bounded (one entity, one join, three filtered surfaces, one form).

**Issues:**

1. **Concurrency policy (Important).** EC-007 states "last write wins, no special concurrency control added beyond what already exists on the user record." The constitution's Quality Gates principle requires optimistic concurrency for entities with concurrent edit risk. User-group membership writes have concurrent-edit risk by construction (two admins editing the same user). The plan must either (a) document the deviation as a complexity-tracking entry, or (b) tighten EC-007 to honor the existing optimistic-concurrency token on the user record. Recommendation: do (b) — EC-007 should say last-writer-wins is enforced via the user record's existing concurrency token, which already exists on `ApplicationUser` writes via Identity. Resolvable at plan time without spec rewrite.

## Testability: 5/5

### Verification
- Every FR maps to at least one acceptance scenario or success criterion.
- SCs are quantified ("100% of cases", "exactly N rows", "next page load").
- US Independent Test descriptions describe concrete reviewer/admin sign-in flows that match the constitution's E2E-testing principle.

**Issues:** none.

## Constitution Alignment

- **I. Clean Architecture:** Spec language stays at the domain/policy level and does not force layer violations. ✓
- **II. Rich Domain Model:** Group entity is thin (Name + identity) which is fine. The invariant "non-admin user has ≥1 group at user-edit submit time" is described as form-level validation; the plan should consider lifting this into the domain (e.g., a `User.SetGroups(role, groups)` method) so the rule is enforced regardless of caller. Optional for spec; flag for plan.
- **III. E2E Testing:** US-level Independent Tests align with Playwright story-level coverage. ✓
- **IV. Schema-First Database:** Spec does not violate; the plan must add `Group` and `UserGroup` to the dacpac (no EF migrations). Flag for plan.
- **V. Specification-Driven Development:** This spec is the artifact. ✓
- **VI. Simplicity:** Flat groups, single dimension, no migration, no rate-limit, no quotas. ✓

**Violations:** none direct. Concurrency policy (above) is a deviation that needs plan-time disposition.

## Recommendations

### Critical (Must Fix Before Implementation)
- none

### Important (Should Fix)
- [ ] Resolve EC-007 concurrency policy vs. constitution Quality Gate requirement. Preferred resolution: tighten EC-007 to "last write wins, enforced via the existing optimistic-concurrency token on the user record." The plan must use a concurrency-token-aware write path for the membership update.

### Optional (Nice to Have)
- [ ] FR-014 references "reviewer-facing applicant and application search". Confirm at plan time which existing search surfaces this maps to (queue search box, dedicated search page, or both). If no reviewer-facing search exists today, restate as a forward-looking requirement that activates if/when search is added.
- [ ] NFR-005 ("audit mechanism") and OQ-001 are intentional deferrals; the plan should pick one (reuse existing or add minimal) and lock the choice before implementation begins.
- [ ] Consider lifting the "≥1 group for non-admin" invariant into the domain entity (per constitution Rich Domain Model) rather than relying on form-level validation alone.

## Conclusion

The spec is implementable as written. The single important note (concurrency policy on membership writes) is a small wording tightening, not a structural change. Optional notes are plan-time concerns, not spec defects.

**Ready for implementation:** Yes (after the EC-007 wording tightening, which can happen during planning rather than blocking sign-off here).

**Next steps:** Proceed to `/speckit-plan`. During plan, resolve the three notes above and add a Complexity Tracking entry only if the chosen approach materially deviates from the constitution.
