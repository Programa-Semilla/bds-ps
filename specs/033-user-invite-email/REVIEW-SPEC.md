# Spec Review: User invitation / set-password onboarding email

**Spec:** specs/033-user-invite-email/spec.md
**Date:** 2026-06-12
**Reviewer:** Claude (speckit-spex-gates-review-spec)

## Overall Assessment

**Status:** SOUND

**Summary:** Complete, unambiguous, and implementable. Three independently-testable user stories with concrete acceptance scenarios; every FR is verifiable; the four brainstorm decisions and two assumptions are baked in with no open clarifications. Reuses existing token + email seams, so scope is bounded. Ready for planning.

## Completeness: 5/5

### Structure
- Purpose (Input + story "Why"), Functional Requirements, Success Criteria, Edge Cases, Assumptions, Out of Scope, Dependencies, Key Entities all present.
- No placeholder/TBD text.

### Coverage
- Invitation lifecycle (issue/expire/consume/resend/supersede), all-roles applicability, delivery-resilience fallback, and the es-CR rejection path each have FRs + scenarios.
- Edge cases explicit: expired/used/invalid link, resend supersedes, already-onboarded user, single-use reuse, dev-seam bypass.

**Issues:** None.

## Clarity: 5/5

### Language Quality
- MUST/MUST NOT used consistently for requirements. The few "should/may" usages are confined to Assumptions (deliberate planning deferrals), not normative FRs.
- Quantified where it matters: **72 hours**, **single-use**, "no usable password", exact es-CR rejection copy.

**Ambiguities Found:** None blocking. "No usable password" is defined in Assumptions (account exists but cannot authenticate until the invited user sets one; mechanism deferred to planning) — intentional WHAT-vs-HOW boundary.

## Implementability: 5/5

### Plan Generation
- A plan is directly derivable: the spec names the seams it reuses (password-reset token issue/consume, `/Account/ResetPassword`, the email sender + branding, the spec-032 admin screens) and the one parameter to change (token lifetime → 72h for invites).
- Dependencies concrete and already in the codebase; scope is a focused additive change plus the removal of the create-form password field.

**Issues:** None. Planning note: the removal of the temp-password field has a known E2E ripple (admin-create-then-login must traverse the invite flow; the Development `SeedUser` seam keeps password-based bootstrap) — already flagged in the spec's Assumptions/Edge Cases.

## Testability: 5/5

### Verification
- Every Success Criterion is observable end-to-end (create without password, invite link → set password → sign in, resend supersedes, expired/used rejection, admin-visible link fallback).
- SC-006 ties delivery to filtered E2E green for the touched areas (Constitution III + the project delivery bar).

**Issues:** None.

## Constitution Alignment

- **I. Clean Architecture** — invitation token + lifecycle in Domain/Infrastructure (mirrors the existing `PasswordResetToken`); email send in Infrastructure; admin screens/confirmation in Web. No layer violations.
- **II. Rich Domain Model** — single-use/expiry/supersede are token invariants, consistent with the existing reset-token entity.
- **III. E2E (non-negotiable)** — SC-006 + each story's Independent Test demand Playwright coverage.
- **IV. Schema-First** — any token persistence change lives in the dacpac; spec stays WHAT-level (reuse vs. new table deferred to planning).
- **V. SDD** — prioritized, independently testable/deliverable stories.
- **VI. Simplicity / YAGNI** — reuses token + email infra; bulk invites, reminders, and retiring the temp-password reset action are explicitly out of scope.

**Violations:** None.

## Recommendations

### Critical (Must Fix Before Implementation)
- None.

### Important (Should Fix)
- None.

### Optional (Nice to Have)
- [ ] During planning, decide explicitly whether to **reuse** the existing `PasswordResetToken` (parameterized to 72h, distinguished by purpose) or introduce a **separate invitation token** — the spec deliberately leaves this open. The "resend invalidates prior unused" requirement (FR-007) is the main constraint to honor either way.
- [ ] During planning, confirm the delivery path (direct-send like `ForgotPasswordEmail` vs. the application-shaped outbox) — the spec leans direct-send in its (non-normative) notes.

## Conclusion

Sound and implementable as written; deferred items are genuine planning decisions, not spec gaps.

**Ready for implementation:** Yes (after planning)

**Next steps:** Proceed to `/speckit-plan` — pin the token reuse-vs-new decision, the delivery path, the "no usable password" technique, and the admin create-form / E2E-bootstrap changes.
