# Spec Review: Fund Process Reception Windows + Applicant Timing UX

**Spec:** specs/044-process-reception-windows/spec.md
**Date:** 2026-06-21
**Reviewer:** Claude (speckit-spex-gates-review-spec)

## Overall Assessment

**Status:** SOUND

**Summary:** A well-bounded, single-concern evolution spec with every cross-round open decision (§28.11, §28.12) resolved and recorded. Ready for planning.

## Completeness: 5/5

### Structure
- All required sections present: Summary/Purpose, Functional Requirements, Success Criteria, Edge Cases, Assumptions, Dependencies, Out of Scope, Key Entities.
- No placeholder text, no TBD markers, no [NEEDS CLARIFICATION].

### Coverage
- 17 FRs grouped by user story; 5 prioritized, independently-testable user stories with acceptance scenarios.
- Error/refusal behavior explicitly specified (FR-009 typed reasons; FR-013 disabled-button explanation).
- 7 edge cases covering overlaps, inactive windows, mid-session config changes, boundary/timezone drift, and trapped drafts.

**Issues:** None.

## Clarity: 5/5

### Language Quality
- Requirements use MUST consistently; boundary semantics stated precisely (`start ≤ now < end`, start-inclusive/end-exclusive).
- Each refusal reason is enumerated (before-first / between / all-closed) rather than left as "appropriate message."
- One deliberate ambiguity in the source requirement (the "global process period") is explicitly resolved and documented as dropped — turning a latent ambiguity into a recorded decision.

**Ambiguities Found:** None blocking. The phrase "live countdown" (FR-011) implies a continuously-updating display; this is an interaction detail for the plan, not a spec ambiguity, since the underlying boundary instant is fully specified by FR-012.

## Implementability: 5/5

### Plan Generation
- The Fund→Process→Group→Application resolution chain and the exact gating points (submission + new-draft creation) are named.
- Dependencies map cleanly to shipped specs (029/030/031/012/024) and the existing submission pipeline.
- Scope is single-concern and explicitly bounded; the general ProcessEvent shaping (US5) is scoped to schema-only, avoiding a generic-calendar-CMS overreach.
- Timezone strategy is concrete (`America/Costa_Rica`, one setting, never per-fund), removing the largest implementation risk.

**Issues:** None. The removal of `SolicitudWindowDays` (FR-008 + Assumptions) is a clear, scoped cleanup with a stated rationale (single consumer removed).

## Testability: 5/5

### Verification
- SC-001…SC-007 are measurable and map to acceptance scenarios; boundary behavior (SC-002) is verifiable at the exact start/end second.
- SC-005 ("existing submission E2E passes unchanged") gives a concrete backward-compatibility gate.
- Clock-dependent criteria are testable via the project's existing frozen-clock pattern (the spec assumes evaluation in CR time, which the gating service centralizes).

**Issues:** None.

## Constitution Alignment

- **Clean Architecture:** ProcessEvent as a domain entity + a pure window-evaluation policy fits the Domain/Application split (no implementation prescribed, but the shape is compatible).
- **Rich Domain Model:** window-state evaluation belongs in a domain/policy unit, consistent with Principle II.
- **Schema-First DB:** new `dbo.ProcessEvents` table via dacpac; column drop of `SolicitudWindowDays` is a schema-project change — aligned with Principle IV.
- **E2E Non-Negotiable:** every user story has acceptance scenarios suitable for Playwright; SC-005 protects the existing suite.
- **Simplicity/YAGNI:** dropping global dates and scoping non-reception event types to schema-only is a direct application of Principle VI.

**Violations:** None.

## Recommendations

### Critical (Must Fix Before Implementation)
- None.

### Important (Should Fix)
- None.

### Optional (Nice to Have)
- [ ] During planning, decide whether the applicant countdown ticks client-side or renders server-computed remaining-time (interaction detail; FR-011/FR-012 already pin the data).
- [ ] During planning, confirm no residual reader of `SolicitudWindowDays` exists beyond the submission gate before dropping the column (Assumptions already flags this; verify in code at plan time).

## Conclusion

The spec is complete, unambiguous, implementable, and testable, with all open decisions resolved and constitution-aligned. The two optional notes are planning-phase confirmations, not spec defects.

**Ready for implementation:** Yes (after `/speckit-plan`).

**Next steps:** User review of the written spec → `/speckit-plan`.
