# Spec Review: Funds-Usage Evidence Inbox

**Spec:** specs/041-evidence-inbox/spec.md
**Date:** 2026-06-19
**Reviewer:** Claude (speckit-spex-gates-review-spec)

## Overall Assessment

**Status:** SOUND

**Summary:** A tightly-scoped navigation + access-mode feature that reuses existing seams (executed-agreement state, Process Active/Closed status, reviewer-scope rule, spec-036 evidence stage). Requirements are testable, scope is well-bounded, and the three brainstorm clarifications are resolved with explicit assumptions. Ready for planning.

## Completeness: 5/5

### Structure
- All mandatory sections present: User Scenarios & Testing, Requirements, Success Criteria. Plus Summary, NFRs, Key Entities, Assumptions, Dependencies, Out of Scope.
- No placeholder/TBD text remains.

### Coverage
- 10 functional requirements + 2 NFRs cover the inbox, the process-close de-listing, the read-only mode (UI + server-side), access control, es-CR copy, and data preservation.
- Edge cases enumerated (reopen, executed-into-closed, empty, mid-upload race, with/without evidence).
- Success criteria cover navigation, listing correctness, read-only enforcement, access control, and no-regression.

## Clarity: 5/5

### Language Quality
- Requirements use MUST consistently; no "should/might/probably/fast/user-friendly" hedging.
- "Process closes" and "results should be gone" — both potential ambiguities — are pinned down in Assumptions (maps to Process `Closed`; "gone" = de-listed + read-only, not deleted).
- Admin behavior under a closed process is made explicit (frozen read-only like everyone; group bypass only affects *which* apps list).

**Ambiguities Found:** None blocking.

## Implementability: 5/5

- Plan is generatable: a new group-scoped inbox projection + controller + view + role-gated sidebar entry, plus a process-status-driven read-only gate on the existing evidence controller/view.
- Dependencies identified (specs 036, 016; Process status; sidebar nav).
- NFR-002 explicitly forbids new state/schema/deps, keeping scope small and aligned with the existing `ProcessStatus`.

## Testability: 5/5

- Every user story has an Independent Test and Given/When/Then scenarios that map directly to Playwright E2E flows (constitution III).
- Success criteria are measurable and technology-agnostic (click-count, 100%/0% listing, 100% rejection, no-regression).

## Constitution Alignment

- **I. Clean Architecture** — inbox query belongs in Application/Infrastructure; read-only gate at the Web controller boundary. Spec stays behavioral, no layer violations implied.
- **II. Rich Domain Model** — no new state transitions; the read-only rule is an authorization/visibility gate over an existing status, not new domain behavior. Acceptable.
- **III. E2E (non-negotiable)** — user stories are browser-testable; SC-005 pins no-regression on spec-036 flows.
- **IV. Schema-First** — NFR-002 mandates no schema change. Compliant.
- **V. SDD** — prioritized, independently testable user stories. Compliant.
- **VI. Simplicity/YAGNI** — search/pagination explicitly deferred; no new deps/state/schema. Compliant.

**Violations:** None.

## Recommendations

### Critical (Must Fix Before Implementation)
- None.

### Important (Should Fix)
- None.

### Optional (Nice to Have)
- [ ] Consider noting (in plan) that the pre-existing entry point — the evidence link on the funding-agreement panel (`FundingAgreement/Details`) — naturally lands on the now-read-only page when the process is closed; confirm that link's continued presence is acceptable (it is, since the page-level mode in FR-006 governs behavior regardless of entry point). Behavioral, not a spec gap.
- [ ] Plan may want to state the inbox's row ordering (e.g., most-recently-executed first) — left to implementation; not a spec requirement.

## Conclusion

The spec is complete, unambiguous, implementable, and testable, with clean constitution alignment and no new state/schema/deps. The two optional notes are plan-level considerations, not spec defects.

**Ready for implementation:** Yes (after `/speckit-plan`).

**Next steps:** User reviews the spec, then `/speckit-plan` to produce the technical design and task list.
