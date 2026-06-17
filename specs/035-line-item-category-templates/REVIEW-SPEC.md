# Spec Review: Line-Item Category Templates, Per-Item Impact, and Quotation Reuse

**Spec:** specs/035-line-item-category-templates/spec.md
**Date:** 2026-06-12
**Reviewer:** Claude (speckit-spex-gates-review-spec)

## Overall Assessment

**Status:** SOUND

**Summary:** A well-bounded, internally consistent reshape of the applicant submission flow. All four user stories are independently testable, requirements are concrete and verifiable, the teardown is scoped precisely, and scope boundaries are explicit. Ready for planning.

## Completeness: 5/5

### Structure
- All mandatory sections present: Overview, User Scenarios & Testing, Requirements (functional + key entities), Success Criteria, Assumptions, Out of Scope.
- Edge Cases enumerated with concrete expected behavior (no "TBD").

### Coverage
- All three changes (category fields, per-item impact, quotation reuse) plus the cross-surface display and the teardown are each covered by FRs and at least one acceptance scenario.
- Error/blocked-submission behavior is specified (FR-006, SC-006, edge cases).

**Issues:** None.

## Clarity: 5/5

### Language Quality
- Requirements use MUST consistently; no "should/might/could" hedging on normative requirements.
- "Reuse" is defined precisely (share vendor + document, per-item price; editing one does not affect others) — the historically ambiguous part of this feature is pinned down.
- The teardown lists exactly what is removed vs. preserved (impact-template gating removed; minimum-quotations rule + required-field flags kept).

**Ambiguities Found:** None blocking. Minor: the spec uses existing domain element names (Category, impact templates, Plantilla, funding-agreement document) as shared stakeholder vocabulary. This is consistent with every prior spec in this repo and aids reviewer precision rather than prescribing implementation.

## Implementability: 5/5

- A plan can be generated directly: the affected aggregates (Category, Item, Quotation, Plantilla/ProcessPlantilla), the new field/value concepts, and the read surfaces are all identifiable.
- Dependencies are realistic and called out (supplier catalog, multi-currency snapshotting, funding-agreement generation, line-code assignment continue unchanged in their own rules).
- Greenfield assumption removes migration risk; the schema-first (dacpac) and Clean Architecture constraints are compatible with the described changes.
- Scope is large but cohesive (single shared UI flow) and the user-story slicing gives natural checkpoints.

**Issues:** None. Note for planning: FR-012's "no dead code" obligation makes the teardown a first-class deliverable — the plan should include the explicit removal task list and a search-based verification step (already mirrored in SC-003).

## Testability: 5/5

- Every success criterion is measurable and technology-agnostic (submit succeeds/blocked, five-item capture with one shared document, codebase search finds zero references, five named surfaces render the data, 100% es-CR copy).
- Acceptance scenarios are Given/When/Then and independently runnable per the constitution's E2E principle.

**Issues:** None.

## Constitution Alignment

- **I. Clean Architecture** — no violation; layer placement deferred to plan.
- **II. Rich Domain Model** — compatible; category-field clearing on category change, document-retention-until-last-reference, and per-item impact requirement are entity invariants the plan will place on the aggregates.
- **III. E2E (non-negotiable)** — each user story carries an Independent Test; delivery bar (filtered E2E) noted.
- **IV. Schema-First** — new tables/columns will be dacpac edits; greenfield means no backfill scripts needed.
- **V. SDD** — this review is part of that workflow.
- **VI. Simplicity / YAGNI** — strong alignment: conditional fields, file/dropdown field types, cross-application reuse, and a standalone shared template catalog are all explicitly deferred to Out of Scope; the ValidationRules seam stays dormant rather than being built out.

**Violations:** None.

## Recommendations

### Critical (Must Fix Before Implementation)
- None.

### Important (Should Fix)
- None.

### Optional (Nice to Have)
- [ ] During planning, confirm the exact list of "AI quote-comparison context" fields that should include category values, so the redaction/PII boundary (spec 020) is respected when new free-text category fields flow into the prompt.
- [ ] During planning, decide whether deactivating (not deleting) the last impact template is itself guarded, given per-item impact is now required (edge case "no active impact templates exist").

## Conclusion

The spec is sound, complete, and implementable. The two optional notes are planning-phase refinements, not spec defects.

**Ready for implementation:** Yes (via `/speckit-plan` first — this is a multi-aggregate, multi-surface feature that warrants a plan).

**Next steps:** Proceed to `/speckit-plan`.
