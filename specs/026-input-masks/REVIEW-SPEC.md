# Spec Review: Structured-Field Input Masks

**Spec:** specs/026-input-masks/spec.md
**Date:** 2026-05-24
**Reviewer:** Claude (speckit-spex-gates-review-spec)

## Overall Assessment

**Status:** SOUND

**Summary:** Complete, testable, and implementable. Three independently-deliverable user stories, no `[NEEDS CLARIFICATION]` markers, all open questions resolved before writing. Two non-blocking planning notes (domain placement of the validation invariant; Profile editability) to settle in `/speckit-plan`.

## Completeness: 5/5

### Structure
- All required sections present: Summary/Purpose, Functional Requirements, Success Criteria, Error Handling (folded into Edge Cases + FR-014/015), Edge Cases, Dependencies, Out of Scope, Assumptions.
- Recommended sections included: Key Entities, Assumptions, Dependencies, Out of Scope.
- No placeholder/TBD text.

### Coverage
- 20 functional requirements grouped by concern (mechanism, catalogue, person, supplier, validation, constraints).
- Error/empty/optional cases enumerated with exact es-CR messages.
- 9 edge cases, each with expected behavior.
- 8 measurable success criteria.

## Clarity: 5/5

Requirements use MUST consistently; no "should/might/fast/user-friendly" vagueness. Mask shapes given as concrete patterns. The one inherent ambiguity (cédula jurídica vs NITE share a 10-digit shape) is explicitly called out and resolved by the persisted type.

## Implementability: 5/5

A plan can be generated directly: surfaces named (Register, admin user create/edit, Profile, supplier add/lookup), the extension point is the existing in-repo masking script, schema change scoped to two nullable columns via the dacpac (Principle IV), domain gains one enum. Scope is a single coherent feature.

## Testability: 5/5

Each user story has an Independent Test and Given/When/Then scenarios. SC-001..008 are verifiable (format per type, character rejection, round-trip, hyphenation-tolerant lookup match rate, form coverage, single-entry extensibility, Spanish copy, E2E green).

## Constitution Alignment (v1.0.0)

- **I. Clean Architecture** — respected: Domain enum, server-side validation, Web view selectors. ✓
- **II. Rich Domain Model** — see planning note 1: the type↔shape invariant should live in the domain (value object / entity guard), with ViewModel attributes echoing it, not validation logic scattered in controllers.
- **III. E2E (NON-NEGOTIABLE)** — SC-008 + per-US independent tests. ✓
- **IV. Schema-First dacpac** — FR-020 + Dependencies route schema through the source of truth; no EF migrations; pre-production so seeds adjusted via post-deploy scripts. ✓
- **V. SDD** — spec is step 2 of the lifecycle; stories independently testable. ✓
- **VI. Simplicity/YAGNI** — registry justified by the explicit "cualquier otro que exista" requirement; speculative masks (bank/IBAN/postal) pushed Out of Scope. ✓

No violations.

## Recommendations

### Critical (Must Fix Before Implementation)
- None.

### Important (Resolve During Planning)
- [ ] **Domain placement of the identification invariant** (Principle II): plan should put type↔shape validation in the domain (an `Identification` value object or an entity guard method) so ViewModel DataAnnotations and the masking script are surface echoes of a single domain rule, not the source of truth.
- [ ] **Profile editability** (FR-009): the current Profile renders email as read-only / admin-managed. Plan must confirm whether the identification selector + value on Profile is editable by the user or display-only; if display-only, FR-011 (restore type + masked value) still applies but the selector is disabled.

### Optional (Nice to Have)
- [ ] Consider whether the supplier surface should warn (not block) when a 10-digit value's leading digit is atypical for the chosen type (jurídica usually starts `3`), as a soft hint — deferred unless wanted.

## Conclusion

The spec is sound and ready to plan. The two important items are plan-level design decisions, not spec defects.

**Ready for implementation:** Yes (after `/speckit-plan` resolves the two planning notes)

**Next steps:** `/speckit-plan` — fold the two planning notes into the technical design and Constitution Check.
