# Spec Review: Supplier Branch Location Cascade (Provincia → Cantón → Distrito)

**Spec:** specs/025-supplier-location-cascade/spec.md
**Date:** 2026-05-22
**Reviewer:** Claude (speckit-spex-gates-review-spec)

## Overall Assessment

**Status:** SOUND

**Summary:** Well-scoped, testable, and grounded in existing spec-021 infrastructure. Completes documented unfinished work (FR-014 cascade never wired) and adds one new catalog level. No [NEEDS CLARIFICATION] markers; ready for planning.

## Completeness: 5/5

### Structure
- Background, prioritized user stories, edge cases, functional requirements, key entities, success criteria, and assumptions all present.
- No placeholder/TBD text.

### Coverage
- Three branch-location surfaces enumerated (FR-010).
- Data sourcing/idempotency (FR-001..003), referential consistency (FR-005), all-or-none at data layer (FR-006), display continuity (FR-013), and legacy-branch safety (FR-014) all covered.
- Edge cases include province-changed-mid-edit, fetch failure, forged identifiers, catalog completeness, legacy branches.

## Clarity: 5/5

### Language Quality
- Requirements use MUST consistently; no "should/might/fast/user-friendly" vagueness.
- "Narrows" cascade behavior defined concretely in acceptance scenarios.

**Ambiguities Found:** None blocking. One soft spot intentionally deferred:
1. Exact distrito count / source revision — explicitly framed as confirm-at-planning in Assumptions rather than asserted. Acceptable; tracked.

## Implementability: 5/5

- Reuses concrete, already-existing infra (province/cantón catalogs, FK columns, cascade endpoint + JS + partial). Plan generation is straightforward — mostly a fourth tier mirroring an existing third tier plus three form wirings.
- Dependencies clear; scope manageable; no new managed dependency implied.

## Testability: 5/5

- SC-001..009 are observable and verifiable through the UI and persistence.
- Each user story is independently testable (constitution Principle V) and maps to E2E coverage (Principle III).

## Constitution Alignment

- **I. Clean Architecture** — location invariant lives on the domain entity; catalog is data. Aligned.
- **II. Rich Domain Model** — FR-005/FR-006 push the cross-level consistency rule onto the entity (`SetLocation`-style method), not controllers. Aligned.
- **III. E2E (non-negotiable)** — three independently testable stories; plan/tasks must produce per-story Playwright coverage driving the real applicant/admin journeys.
- **IV. Schema-First DB** — new Districts table via `.sql`, seed via post-deployment script (FR-001/003). Aligned.
- **VI. Simplicity** — extends existing pattern, no speculative abstraction, no new dependency. Aligned.

**Violations:** None.

## Recommendations

### Critical (Must Fix Before Implementation)
- None.

### Important (Should Fix in Plan)
- [ ] Constitution quality gate "all validation errors displayed at once": plan MUST aggregate the all-three-required messages into the existing ModelState/summary pattern (consistent with spec 023), not surface them one at a time.
- [ ] Plan MUST include an authoritative-source step for the distrito seed (FR-002) plus a count/FK-integrity validation against the version-matched 84-cantón catalog (SC-007) — derive the list, do not hand-type from memory.

### Optional (Nice to Have)
- [ ] Consider whether the legacy composed display string (FR-013) should order as "Distrito, Cantón, Provincia" (most-specific first) vs. the reverse; pick one in the plan and keep it consistent across surfaces.

## Conclusion

Sound and implementable. The single real risk is data sourcing for the distrito catalog, which the spec already flags and defers to planning with a validation gate.

**Ready for implementation:** Yes (after the two Important items are addressed in the plan).

**Next steps:** Proceed to `/speckit-plan`. Optionally `/speckit-clarify` first — not required; no critical ambiguities remain.
