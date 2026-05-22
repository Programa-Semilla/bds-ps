# Spec Review: Feedback Session May-13 (021)

**Spec:** specs/021-feedback-session-may13/spec.md
**Date:** 2026-05-13
**Reviewer:** Claude (speckit-spex-gates-review-spec)

## Overall Assessment

**Status:** SOUND

**Summary:** All required sections present; every FR uses MUST/MUST NOT verbiage with concrete artefacts; every SC is measurable; 18 edge cases enumerated; 10 explicit open questions explicitly deferred to `/speckit-plan`; constitution alignment is clean. Scope is large (34 FRs, 16 SCs, 8 user stories) by deliberate stakeholder directive ("single shot"), and the spec internally prioritizes via P1/P2/P3 so the plan phase can sequence delivery cleanly.

## Completeness: 5/5

### Structure
- All required sections present: Purpose-by-Input, User Scenarios (with priorities), Edge Cases, Functional + Non-Functional Requirements, Success Criteria, Assumptions, Dependencies, Out of Scope, Open Questions, Key Entities.
- No placeholder text remaining; no "TBD" markers.
- Stakeholder-aligned scope boundaries explicit in Out of Scope.

### Coverage
- All 26 meeting items traceable into FRs (verified by re-reading the PDF themes against the FR list).
- Error and edge cases covered: 18 enumerated edges including bootstrap empty-state, detach-with-active, expiry mid-edit, autosave failure, token reuse, role-escalation attempt, code-collision retry, no-production-data cutover, foreign address rejection.
- Success criteria specified for every priority story and every architectural shift.

**Issues:** None blocking.

## Clarity: 5/5

### Language Quality
- Requirements use MUST / MUST NOT / MAY consistently — no "should", no "might".
- Specific values pinned: token TTL 60 min, retries max 5, P95 300 ms, regex pattern, base32 alphabet, T-72h/T-24h/expiry cadence, CR phone mask format, single-key disclaimer.
- "Process", "Plantilla", "Application", "ImpactTemplate", "AdminAuditEvent" used precisely against existing domain entities.

**Ambiguities Found:** None worth flagging at this gate.

Minor observations that are not blockers (each already implicitly resolved via Open Question or Edge Case):
- FR-008 PublicCode collision retry caps mentioned only in Edge Cases ("three failed attempts → log + throw"); FR text itself is silent on the cap. *Treat as plan-phase pin; spec language is internally consistent.*
- FR-016 autosave behaviour during in-flight submit is captured by FR-017 ("submit MUST remain blocked until the banner clears") + the autosave-failure edge case. *Already resolved.*

## Implementability: 5/5

### Plan Generation
- Plan can generate cleanly: each user story is independently testable per Constitution Principle III; each story has clear acceptance scenarios; dependencies on prior specs (008/011/012/014/015/016/017/019) explicitly listed.
- Constraints realistic: no new managed dependencies (NFR-005), existing SMTP reused (Dependencies), dacpac handles schema (NFR-001 + Constitution Principle IV).
- Scope is large but **explicitly partitioned by P1/P2/P3**: P1 is the load-bearing foundation (Process + Plantilla + Impact-on-Application + applicant journey + SupplierAdmin); P2/P3 stack on top.
- Spec preemptively captures 10 Open Questions that are plan-phase pins, not spec gaps.

**Issues:** None blocking.

## Testability: 5/5

### Verification
- Every SC is measurable: time bounds (≤ 3 min, ≤ 90 s, P95 300 ms, ≤ ±1 h granularity), absence assertions (grep returns 0), 100 % pass-rate clauses, regex match, snapshot equality.
- Every acceptance scenario uses Given/When/Then with concrete inputs and observable outputs.
- Constitution Principle III (E2E NON-NEGOTIABLE) reflected in NFR-004 + SC-016 + FR-021's explicit E2E coverage requirement on the bug fix.

**Issues:** None.

## Constitution Alignment

Constitution v1.0.0 principles all satisfied:

- **I. Clean Architecture** — FRs use domain-entity language (`Process`, `Plantilla`, `Application`, `Item`, `Supplier`, `SupplierBranch`, `Province`, `Canton`, `PasswordResetToken`); no Web/Infrastructure leakage into spec.
- **II. Rich Domain Model** — FR-001 / FR-004 / FR-005 / FR-006 establish entity-level behaviour (Process owns expiry overrides, Plantilla copy-on-assign, Impact relocation, expiry hard-block). Behaviour described at the aggregate, not in services.
- **III. End-to-End Testing (NON-NEGOTIABLE)** — NFR-004 + SC-016 + FR-021 + Independent Test on every user story. Project rule explicitly reaffirmed.
- **IV. Schema-First Database Management** — NFR-001 mandates dacpac for all new tables; no EF migration mentioned anywhere.
- **V. Specification-Driven Development** — This artefact is the spec; 8 P1/P2/P3 user stories with independent-test clauses.
- **VI. Simplicity and Progressive Complexity** — Scope is large by deliberate stakeholder directive; YAGNI applied in Out of Scope (BCCR, AI, OTP, tour, email-change request, foreign addresses, visual-regression tooling). Each deferral is explicit, not implicit.

**Violations:** None.

## Cross-Artifact Consistency

`plan.md` and `tasks.md` do not yet exist for 021 (expected — spec is the first artefact). `/speckit-analyze` is therefore N/A at this gate.

## Recommendations

### Critical (Must Fix Before Implementation)
- None.

### Important (Should Fix)
- None.

### Optional (Nice to Have)
- During `/speckit-plan`, pin FR-008 retry-cap in the FR text itself rather than only in Edge Cases (minor wording tidy).
- During `/speckit-plan`, materialize OQ-1 through OQ-10 as plan-phase decisions in a single "Pinning Open Questions" section to keep the plan auditable.
- During `/speckit-plan`, sequence US1 (Process/Plantilla/Impact-on-Application) first to unblock US2–US8, then deliver US3 (SupplierAdmin role) and US2 (applicant journey) in parallel.

## Conclusion

The spec is **sound** and ready for implementation planning. Scope is large but priorities are clear, edge cases are comprehensive, constitution alignment is clean, and the 10 open questions are scoped as plan-phase pins rather than spec gaps. No corrective iteration required at this gate.

**Ready for implementation:** Yes — proceed to `/speckit-plan`.

**Next steps:**
1. Surface spec path to the user for review (brainstorm-skill user review gate).
2. On user approval, generate `review_brief.md` for stakeholder reviewers.
3. Commit `specs/021-feedback-session-may13/` and offer `/speckit-plan` as the next step.

---

## Addendum Review: User Story 9 — applicant-initiated delete/withdrawal

**Date:** 2026-05-21
**Reviewer:** Claude (speckit-spex-gates-review-spec)
**Scope:** US9, FR-035–FR-041, SC-017/SC-018, 3 new edge cases, 2 new assumptions, 3 new out-of-scope items.

**Status:** NEEDS WORK — one Important issue (terminology mismatch with the actual reviewer model). Everything else sound.

### Completeness: 5/5
US9 has Why/Independent-Test/5 acceptance scenarios; FR-035–041 cover delete, withdraw, state gating, placement, confirmation, notification, ownership; SC-017/SC-018 are measurable; edge cases cover no-reviewer, mid-flight state change, idempotent repeat. No placeholders.

### Clarity: 4/5
MUST/MUST NOT used throughout; labels, event name, and states pinned. One ambiguity — see Important issue below.

### Implementability: 4/5
Reuses production-proven soft-delete (`Application.SoftDelete()`, `ExcludeDeleted`) and the spec-021 outbox — no schema change. Blocked only by the recipient-resolution mismatch.

### Testability: 5/5
SC-017/SC-018 give absence assertions (zero emails, affordance absent from HTML), exact-count assertions (exactly one email), 403/redirect on direct POST, smtp4dev capture. Maps cleanly to E2E.

### Important issue (must reconcile before plan)

**I-1 — "assigned reviewer" is not a real concept.** FR-040, US9 scenarios 2–3, and SC-017 all hinge on an "assigned reviewer" and a "skip if no reviewer is assigned" branch. The codebase has **no per-application reviewer assignment**:
- `Application` entity has no `AssignedReviewerId`/`ReviewerId` (`Application.cs`).
- Reviewer access is spec-016 group-overlap: any Reviewer-role user in a group the applicant shares can open it (`ReviewController.cs:229-244`, `ApplicationRepository.ApplicantSharesAnyGroupAsync`).
- `APPLICATION_SUBMITTED_REVIEWER` already resolves to the **whole reviewer pool of the application's stage group(s)**, not one assignee (`NotificationRecipientResolver.cs:76-121`).

Consequence: there is no "unassigned" state to gate on. The only coherent recipient set for the withdrawal email is the same stage-group reviewer pool that submission notifies; the natural "skip" case is an **empty pool** (a stage group with zero Reviewer-role members). This **reverses the premise of brainstorm decision OQ-1** ("skip if unassigned" vs "notify the eligible group") — the eligible-group fan-out is in fact the *only* mechanism that exists. Requires a user decision, then a wording fix to FR-040 / scenarios 2–3 / SC-017.

### Recommendations
- **Important:** Resolve I-1 with the user, then reword FR-040, US9 scenarios 2–3, and SC-017 to target "the reviewer pool for the Application's current stage group (the same recipient set as `APPLICATION_SUBMITTED_REVIEWER`)", with skip = empty pool.
- **Optional:** Add `APPLICATION_WITHDRAWN_BY_APPLICANT` to the `NotificationEvent` enum list reference (`NotificationEvent.cs`) during plan.
- **Optional:** Confirm the withdrawal email uses the `RecipientBucket.Reviewer` variant + allowlist + idempotency key extended with the version/withdrawal discriminator.

**Ready for implementation:** No — after I-1 reconciliation + re-review.

### Re-review (2026-05-21, iteration 2)

**Status:** SOUND.

I-1 reconciled with the user. Decision: withdrawal notifies the **stage-group reviewer pool** (the `APPLICATION_SUBMITTED_REVIEWER` recipient set) **only when the Application is `UnderReview`**; a plain `Submitted` withdrawal notifies no reviewer; an empty pool is a natural no-op. The "assigned reviewer" language is gone. Updated: US9 narrative + Independent Test + scenarios 2–3, FR-039, FR-040, SC-017, SC-018, two edge cases, and the reviewer-resolver assumption. All now reference the real group-overlap model. No remaining inconsistencies; reuses production-proven soft-delete + outbox with no schema change.

**Ready for implementation:** Yes — proceed to user review gate, then `/speckit-plan`.
