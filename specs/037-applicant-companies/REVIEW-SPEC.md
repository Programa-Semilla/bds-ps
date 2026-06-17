# Spec Review: Applicant Companies — controlled company selection on submission

**Spec:** specs/037-applicant-companies/spec.md
**Date:** 2026-06-17
**Reviewer:** Claude (speckit-spex-gates-review-spec)

## Overall Assessment

**Status:** SOUND

**Summary:** The spec is complete, unambiguous, and implementable. All four foundational decisions (greenfield rollout, batch-CSV scope, soft-archive semantics, draft-time editability) were resolved during brainstorming and encoded as concrete requirements/assumptions. No clarification markers remain.

## Completeness: 5/5

### Structure
- All mandatory sections present: User Scenarios & Testing, Requirements, Success Criteria.
- Recommended sections included: Edge Cases, Key Entities, Assumptions, Out of Scope, Open Questions.
- No placeholder/TBD text.

### Coverage
- 20 functional requirements span company management, applicant selection, and data integrity/security.
- Both selection scenarios (single auto-select, multi explicit-choice) and the zero-company case are covered.
- Error/abuse paths covered: forged requests (FR-019), cross-applicant ownership (FR-018), archived-while-draft (FR-020), duplicate names (FR-003).
- Historical preservation has its own user story (US3) with explicit before/after scenarios.

## Clarity: 5/5

### Language Quality
- Requirements use MUST consistently; outcomes are concrete.
- The single vs. multiple company behaviors are specified to the level of placeholder copy ("Seleccione una empresa…") and default-selection state.
- "At least one active company" floor and snapshot-refresh timing are stated precisely.

**Ambiguities Found:** None blocking. The one open item (admin UI placement for the company list) is explicitly labeled a HOW decision deferred to planning and does not affect any requirement.

## Implementability: 5/5

- A plan can be generated directly: new aggregate + nullable FK + snapshot reuse of an existing column, reusing identified existing seams (submission create flow, admin user create/edit/batch, searchable dropdown, admin audit, es-CR localization).
- Greenfield assumption removes migration/backfill risk; nullable reference keeps pre-existing rows valid.
- Scope is bounded to the Applicant role with an explicit Out of Scope list.

## Testability: 5/5

- Every user story has independent-test guidance and Given/When/Then acceptance scenarios.
- Success criteria SC-001…SC-008 are measurable and technology-agnostic (e.g., "0% created via free-text", "last active company archival blocked 100% of the time").
- The security requirements (FR-018/019) are verifiable via server-side rejection tests independent of the UI.

## Constitution Alignment

Aligned with FundingPlatform Constitution v1.0.0:
- **I. Clean Architecture** — spec stays at requirement altitude; layering deferred to plan.
- **II. Rich Domain Model** — invariants (name length/uniqueness, archive floor, snapshot freeze, ownership) are naturally domain-entity responsibilities; the spec frames them as system rules, not service logic.
- **III. E2E Testing** — four independently testable user stories map to Playwright suites (golden + error paths).
- **IV. Schema-First** — new table/column are schema additions the plan will route through the dacpac; spec does not prescribe EF migrations.
- **V. SDD** — this artifact; user stories prioritized and independently deliverable.
- **VI. Simplicity/YAGNI** — name snapshot reused instead of company versioning; greenfield avoids speculative backfill; company limited to a single attribute.

**Violations:** None.

## Recommendations

### Critical (Must Fix Before Implementation)
- None.

### Important (Should Fix)
- None.

### Optional (Nice to Have)
- During planning, decide the admin company-list surface (inline on user Edit vs. dedicated sub-surface) — already captured as an Open Question.
- During planning, confirm the audit-event naming prefix (e.g., `company.*`) to mirror existing `fund.*`/`process.*`/`funds_evidence.*` conventions.

## Conclusion

The spec is sound and ready for planning/implementation. Decisions that typically cause drift were resolved up front and written into requirements.

**Ready for implementation:** Yes

**Next steps:** Proceed to `/speckit-plan`.
