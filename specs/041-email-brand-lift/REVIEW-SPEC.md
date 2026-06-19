# Spec Review: ALIA Transactional Email Brand UI-Lift

**Spec:** specs/041-email-brand-lift/spec.md
**Date:** 2026-06-19
**Reviewer:** Claude (speckit-spex-gates-review-spec)

## Overall Assessment

**Status:** SOUND

**Summary:** A well-bounded UI + copy lift of the email subsystem with three additive emails. Requirements are specific, testable, and implementable against existing seams (spec 021 outbox, spec 028 CTA routing, spec 037 brand). Three open questions are recorded as deliberate deferrals; only one (OQ-2) carries a genuine planning risk and it is non-blocking.

## Completeness: 5/5

### Structure
- Overview/Purpose, Functional + Non-Functional Requirements, Success Criteria, Edge Cases, Assumptions, Dependencies, Out of Scope, and Open Questions all present.
- No TBD/placeholder text; no `[NEEDS CLARIFICATION]` markers.

### Coverage
- FRs cover the design system (FR-001..006), copy/naming (FR-007..009), coverage of all existing emails (FR-010), the three new emails (FR-011..013), and new-event integration (FR-014).
- Error/degradation paths covered (no-link, images-blocked, dark mode, allowlist drop, Outlook).

## Clarity: 5/5

### Language Quality
- Requirements use MUST consistently; metrics where they matter (≈600px, WCAG AA, "exactly once").
- "Light polish" is bounded by explicit invariants (meaning, variables, warnings, automatic-message notes preserved) — not a vague term in this context.

**Ambiguities Found:** None blocking. The one inherent unknown (FR-013 trigger/recipient) is explicitly quarantined in OQ-1 and de-risked by shipping the template without a live trigger.

## Implementability: 5/5

- Every requirement maps to an existing, named seam (shared layout + partials, outbox event identity + idempotency key + CTA route, direct-send identity email, hosted-asset base URL). Plan generation is straightforward.
- Scope is a single cohesive subsystem; no decomposition needed.
- No new managed dependencies / no build step (NFR-006) keeps it within project conventions.

## Testability: 5/5

- Success criteria are measurable via the existing mail-capture harness (SC-001..008): brand rendering, zero-variable-loss, no-invented-URL, images-blocked legibility, palette/footer consistency, per-event green E2E, plain-text twin presence, exactly-once delivery.
- Each user story has an independent test path.

## Constitution Alignment

- **III. E2E (non-negotiable):** SC-006 requires filtered mail-capture E2E green for changed/added events. ✅
- **IV. Schema-first:** New notification events follow the spec-028 string-stored pattern (no dacpac change for enum additions). Planner should confirm whether any new payload persistence is required; none is implied. ✅ (verify in plan)
- **V. SDD / independently testable stories:** Stories P1–P4 are standalone slices. ✅
- **VI. Simplicity / YAGNI:** FR-013's live trigger is explicitly deferred rather than speculatively built. ✅

**Violations:** None.

## Recommendations

### Critical (Must Fix Before Implementation)
- None.

### Important (Should resolve during planning)
- [ ] **OQ-2** — Confirm against the application state model whether "entering review" is a distinct transition from submission. If submit→review is atomic, FR-011 ("Solicitud en revisión") would be redundant with the submission receipt and should be re-scoped or dropped. This is the single substantive risk; it does not block the P1 brand lift.

### Optional (Nice to Have)
- [ ] **OQ-3** — Decide the public image-serving path (dedicated email-assets path vs existing static-library path) in the plan; affects FR-002 only mechanically.
- [ ] Consider naming the concrete new event identities in the plan (e.g. `APPLICATION_UNDER_REVIEW_APPLICANT`) for traceability, keeping them out of the spec per WHAT-not-HOW.

## Conclusion

The spec is sound, complete, and implementable. The P1 design-system lift can proceed independently of all three open questions. OQ-1 cleanly gates only the live trigger of the lowest-priority email; OQ-2 is a planning-time verification that could re-scope one P2 email; OQ-3 is mechanical.

**Ready for implementation:** Yes (after OQ-2 is verified in planning; OQ-1 gates only FR-013's live trigger).

**Next steps:** User review of the spec, then `/speckit-plan` — which should resolve OQ-2 and OQ-3 early.
