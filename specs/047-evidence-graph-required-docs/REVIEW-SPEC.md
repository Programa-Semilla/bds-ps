# Spec Review: Evidence Graph & Required-Document Rules

**Spec:** specs/047-evidence-graph-required-docs/spec.md
**Date:** 2026-07-16
**Reviewer:** Claude (speckit-spex-gates-review-spec)

## Overall Assessment

**Status:** SOUND

**Summary:** A complete, testable, well-bounded spec for the largest program slice to date; every scope-level decision was resolved in brainstorming (zero clarification markers), scope is explicitly fenced against P4–P9, and all requirements are verifiable. Two minor notes are carried to `/speckit-plan`, neither blocking.

## Completeness: 5/5

### Structure
- All required sections present: Context/Purpose, User Scenarios (4 prioritized, independently-testable stories), Functional Requirements, Success Criteria, Edge Cases, plus recommended NFRs, Key Entities, Assumptions, Dependencies, Out of Scope, Open Questions.
- No TBD or placeholder text.

### Coverage
- 29 FRs + 5 NFRs cover the evidence graph, per-line allocation, required-doc rules, completeness, closure/reopen, version history, reconciliation legs, access control, and audit.
- Error/refusal paths covered: over-allocation, orphaned document, missing-reason replace, closure blocks (a–d), out-of-group/non-executed flat 404.
- Edge cases are concrete (boundary allocation, partial vs full allocation, amount-reduction-below-allocated, no-payment line, reopen-under-changed-rule, concurrency).

## Clarity: 5/5

### Language Quality
- Requirements use MUST throughout; amounts are exact (`decimal(18,2)`, zero tolerance, ₡0.01 smallest difference).
- Potentially-vague term "reconciliation-critical field" is explicitly enumerated (amount/currency/document number/document date).
- The equality chain is stated concretely (FR-024) and cross-referenced by the closure gate (FR-015c).

**Ambiguities Found:** None blocking.

## Implementability: 5/5

- Grounded in existing, named program artifacts (Item, Disbursement, DisbursementEvidence, Category, ChecklistTemplate, storage stack), so plan generation is straightforward.
- Three genuinely open design choices are isolated as OQ-1/2/3 (migration shape, Closed representation, version-chain shape) and correctly deferred to planning — they do not block spec approval.
- Scope is the largest slice in the program (4 stories), but each is an independently deliverable/testable slice per Constitution §V, consistent with 045/046 sizing.

## Testability: 5/5

- Seven measurable Success Criteria map to the seed's AC-002/003/005/008 plus a P1/P2 regression (SC-006) and an access-control assertion (SC-007).
- Every user story has an Independent Test and Given/When/Then acceptance scenarios that translate directly into E2E flows.

## Constitution Alignment

- **I Clean Architecture / II Rich Domain Model:** spec keeps closure, allocation, and completeness as domain behaviour (WHAT), leaving layering to the plan — aligned.
- **III E2E Testing:** all SCs are browser-drivable; each story independently testable — aligned.
- **IV Schema-First:** NFR-004 mandates additive dacpac-only, no EF migrations — aligned.
- **V SDD:** this spec is the artifact; stories are independently deliverable — aligned.
- **VI Simplicity/YAGNI:** required-doc config fenced to per-Category + global default; five other FR-033 axes explicitly deferred as seams; P4–P9 concerns fenced out — strongly aligned.

**Violations:** None.

## Recommendations

### Critical (Must Fix Before Implementation)
- None.

### Important (Should Fix)
- None.

### Optional (Carry to /speckit-plan)
- [ ] **Completeness ↔ disbursement-anchored evidence:** make explicit in the plan that a disbursement's Bank Receipt / Invoice (P1) counts toward its paid budget-lines' completeness (FR-006 + FR-010 imply this but don't state it). Decide during OQ-1 (generalize vs coexist) so the completeness query reads both sources.
- [ ] **Scope watch:** four stories in one slice is the program's largest; plan should keep the P1/P2 regression (SC-006) green at each story checkpoint and consider whether US4 (version history) can land as a clean checkpoint independent of US3.

## Conclusion

The spec is sound, complete, and implementable, with strong constitution alignment and no critical or important issues. The two optional notes are planning-phase refinements.

**Ready for implementation:** Yes (after `/speckit-plan`)

**Next steps:** Optionally run `/speckit-clarify` (not needed — zero clarification markers), then `/speckit-plan` to resolve OQ-1/2/3 and produce the technical design + tasks.
