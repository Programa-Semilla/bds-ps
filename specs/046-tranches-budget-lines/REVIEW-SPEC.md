# Spec Review: Tranches & Budget-Lines (Financial Execution P2)

**Spec:** specs/046-tranches-budget-lines/spec.md
**Date:** 2026-07-16
**Reviewer:** Claude (speckit-spex-gates-review-spec)

## Overall Assessment

**Status:** SOUND

**Summary:** Complete, testable, and tightly scoped as slice P2 of the financial-execution program. Requirements are unambiguous and every user story is independently deliverable; the deferral boundaries to P3–P9 are explicit. Ready for planning.

## Completeness: 5/5

### Structure
- Overview, four prioritized user stories, edge cases, functional requirements, key entities, success criteria, assumptions, and dependencies all present.
- No placeholder / TBD text; no `[NEEDS CLARIFICATION]` markers.

### Coverage
- Tranche structure, commit lifecycle, per-line attribution, balance composition, reconciliation, roles, filtering, and audit are all covered by FRs.
- Edge cases enumerate the real failure modes (pre-feature backfill, un-commit-with-payment, split-integrity, over-payment, concurrent-validation race, empty application).

## Clarity: 5/5

### Language Quality
- Consistent MUST phrasing; each FR is a single testable assertion.
- Zero-colón semantics stated precisely (Σ split = amount; Σ tranche = allocation by construction; per-line over-payment ≤ committed).

**Ambiguities Found:** None blocking. One item for planning to pin down (not a spec defect):
1. FR-020 lists both "status" and "validation state" as filter facets. The exact set of line "status" values (uncommitted / committed / paid / validated) is a design detail deferred to `/speckit-plan` — the spec's line lifecycle (US2/US3) already fixes the underlying states.

## Implementability: 5/5

- Cleanly generatable plan: extends spec 045's Disbursement/ledger/projection seams with a new `Tranche`, a disbursement↔line allocation join, and tranche/commit fields on the existing `Item`.
- Dependencies (045, 040, `Item`/allocation total, audit trail) are named. Scope is one coherent slice.
- Derived tranche amount removes a whole reconciliation surface (Σ tranche = allocation is structural), reducing risk.

## Testability: 5/5

- Every user story has an Independent Test and Given/When/Then acceptance scenarios.
- Success criteria are measurable and technology-agnostic (zero colón difference; 100% split integrity; 100% over-payment block; no P1 regression).

## Constitution Alignment

- **I. Clean Architecture** — spec stays at WHAT level; existing-artifact names appear only as reuse anchors.
- **II. Rich Domain Model** — commit/attribution/validation are invariant-bearing behaviors (line lifecycle, obligate-then-pay, over-payment gate) that belong on entities.
- **III. E2E (non-negotiable)** — four independently testable user stories map to E2E flows.
- **IV. Schema-first** — assumptions commit to additive dacpac-only schema, no EF migrations.
- **V. SDD** — prioritized user stories, acceptance scenarios, measurable success criteria.
- **VI. Simplicity/YAGNI** — derived tranche amounts over independently-set amounts; explicit deferrals to P3–P9; no new managed deps.

**Violations:** None.

## Recommendations

### Critical (Must Fix Before Implementation)
- None.

### Important (Should Fix)
- None.

### Optional (Nice to Have)
- [ ] During planning, enumerate the concrete budget-line "status" filter values (FR-020) so US4 filter tests are unambiguous.
- [ ] During planning, resolve the two carried open questions: whether commit gets a ledger entry type (leaning no — off-ledger per FR-009) and the commit-state representation (enum on `Item` vs. separate row).

## Conclusion

The spec is sound, complete, and implementable, and aligns fully with the constitution. The two open questions are correctly scoped as HOW-level planning decisions rather than spec gaps.

**Ready for implementation:** Yes (after `/speckit-plan`)

**Next steps:** Proceed to `/speckit-plan` to produce the technical design and task breakdown.
