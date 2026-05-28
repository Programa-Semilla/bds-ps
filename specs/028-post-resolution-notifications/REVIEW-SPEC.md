# Spec Review: Post-Resolution Email Notifications

**Spec:** specs/028-post-resolution-notifications/spec.md
**Date:** 2026-05-27
**Reviewer:** Claude (speckit-spex-gates-review-spec)

## Overall Assessment

**Status:** SOUND

**Summary:** A tightly-scoped increment to shipped spec 021 that adds 12 counterparty-notification events across the post-`Resolved` flow. Reuses all existing notification infrastructure, introduces zero schema change, and is fully testable. Two refinements (§II Rich Domain Model on the audit append; actor self-notification exclusion) were applied during review.

## Completeness: 5/5

### Structure
- All required sections present: Purpose (Input), User Scenarios + acceptance, Functional Requirements, Success Criteria, Edge Cases, Dependencies, Assumptions, Out of Scope, Open Questions.
- No TBD / placeholder text.
- Clarifications session recorded (5 decisions).

### Coverage
- 12 events each have a trigger (Event Catalog), a recipient rule (Recipient Rules), a CTA target, and at least one acceptance scenario.
- Edge cases cover the genuinely tricky paths: GrantReopenToReview dual-fire, successive appeal messages, regenerate re-fire, withdraw-to-empty-inbox, outcome-driven body copy, actor-as-admin exclusion, multi-reviewer fan-out.

## Clarity: 5/5

- Requirements use MUST consistently; each is specific and bound to a named domain trigger.
- Recipient matrix and Event Catalog tables remove ambiguity about who-gets-what.
- Residual soft phrasing ("context cue" for the rejection body) is explicitly bounded by NFR-003 (no PII / no verbatim reviewer commentary), so it is testable.

## Implementability: 5/5

- Every trigger maps to a verified existing domain method (confirmed by codebase exploration 2026-05-27).
- The single non-notification code change (FR-010 audit row) is isolated and now routed through the domain method per §II.
- Idempotency anchor reuse is sound: 11 triggers already write a VersionHistory row; FR-010 supplies the 12th.
- Scope is one slice (post-`Resolved` notifications), independently planeable.

## Testability: 5/5

- SC-001..008 are measurable: per-US E2E via smtp4dev, integration test for the recipient matrix, double-pass idempotency (incl. dual-fire + successive messages), allowlist fail-closed, brand-grep gate, zero-migration grep gate, P95 budget.
- SC-007 explicitly ties the reported bug to US1 regression coverage.

## Constitution Alignment

- **§I Clean Architecture** — Notification wiring stays in Application/Infrastructure; CTAs reuse Web routes. Aligned.
- **§II Rich Domain Model** — FR-010 amended during review to append the audit row via `Application.AddVersionHistory` (domain method), not a raw service mutation. Aligned.
- **§III E2E (non-negotiable)** — One E2E per user story, driving the real UI journey through the sidecar (SC-001). Aligned and strong.
- **§IV Schema-First** — FR-021 / SC-006: zero new tables, zero dacpac change, zero EF migrations; enum extension stored identically. Aligned.
- **§V SDD** — Spec → plan → tasks → implement; US independently testable. On track.
- **§VI Simplicity / YAGNI** — Declines self-confirmations, digests, OQ-011 fix; defers them explicitly. Aligned.

**Violations:** None remaining (the §II concern was fixed inline).

## Recommendations

### Critical (Must Fix Before Implementation)
- None.

### Important (Should Fix) — applied during this review
- [x] FR-010: append audit row through the domain method (§II), not raw service mutation.
- [x] FR-013a + EC-011: exclude the triggering actor from recipients so a reviewer/admin never receives a copy of their own action.

### Optional (Nice to Have) — defer to planning
- [ ] Confirm during planning which exact `/Applications/{id}/FundingAgreement/` sub-route each applicant-bucket CTA targets (download vs upload vs details surface) — the spec fixes the surface family; the precise action is a planning detail.
- [ ] Decide whether `APPEAL_MESSAGE_*` should carry a short snippet of the message body or only a "new message" cue (NFR-003 leans toward a cue + CTA).

## Conclusion

The spec is complete, unambiguous, implementable, and testable, and it aligns with the constitution after two inline fixes. It correctly frames itself as an additive increment with no schema impact and strong idempotency reuse.

**Ready for implementation:** Yes (after user review).

**Next steps:** User reviews `spec.md`; then `/speckit-plan` to produce the technical design and Constitution Check, followed by `/speckit-tasks`.
