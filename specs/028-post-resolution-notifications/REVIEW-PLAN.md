# Review Guide: Post-Resolution Email Notifications

**Spec:** [spec.md](spec.md) | **Plan:** [plan.md](plan.md) | **Tasks:** [tasks.md](tasks.md)
**Generated:** 2026-05-27

---

## What This Spec Does

The platform emails people as an application moves through submission and review (spec 021), but the moment it reaches `Resolved`, the emails stop. Everything after — the applicant accepting or rejecting the reviewer's decision, the whole appeal back-and-forth, and the convenio signing ceremony — happens in silence. A reviewer literally never learns that an applicant accepted their resolution, which is the bug that kicked this off. This spec adds 12 events so every remaining applicant↔reviewer interaction sends one email to the other party.

**In scope:** 12 new notification events across applicant-response ([US1](spec.md#user-story-1---applicant-response-reaches-the-reviewer-priority-p1)), the appeal lifecycle ([US2](spec.md#user-story-2---the-appeal-lifecycle-is-fully-voiced-priority-p1)), and convenio signing ([US3](spec.md#user-story-3---the-convenio-signing-ceremony-is-fully-voiced-priority-p1)); 24 es-CR Razor partials; reuse of the entire spec-021 outbox/worker/provider/allowlist pipeline.

**Out of scope:** in-app/SignalR/push; digests; notification-preferences UI; the still-deferred stage-granular events; fixing the inherited spec-021 OQ-011 admin-role-change limitation; **any schema/dacpac change**. See [Out of Scope](spec.md#out-of-scope). The self-confirmation decision (the actor never gets a "we got it" email) is the boundary most worth a second opinion.

## Bigger Picture

This is the third layer on the spec-021 notification subsystem (021 shipped the pipeline + 6 events; US9 later added withdrawal; this adds 12 post-`Resolved` events). It deliberately changes nothing about the transport — same transactional outbox, same `EmailDispatchWorker`, same Mailgun/smtp4dev split, same fail-closed allowlist. The interesting design pressure is that the post-`Resolved` flow has shapes the original 6 events never had: a **bidirectional** conversation (appeal messages), a **branching** outcome (appeal resolution → uphold / reopen-draft / reopen-review), and a **multi-step ceremony** (generate → upload → replace/withdraw → approve/reject). The plan absorbs those into the existing single-event-per-row model rather than inventing threading or state machines — worth checking whether that's the right call (see questions below).

---

## Spec Review Guide (30 minutes)

### Understanding the approach (8 min)

Read the [spec Input/summary](spec.md#feature-specification-post-resolution-email-notifications) and [research R-001](research.md#r-001--event-aware-cta-resolution-the-one-cross-cutting-gap). As you read, consider:

- The whole design rests on "notify the counterparty, never self-confirm." Is that the right principle for *every* one of these events, or do some (e.g. the applicant submitting their final response, or uploading a signed convenio) deserve a reassuring "we received it" the way the original submit does?
- Spec 021 hard-wired CTAs to two routes; this spec generalizes to a per-event route template ([R-001](research.md#r-001--event-aware-cta-resolution-the-one-cross-cutting-gap)). Is putting the route next to the subject in `NotificationTemplateBindings` the right home, or should destinations live with recipient logic?
- The increment lives as its own spec (028) rather than folding into 021. Does that match how you want the notification subsystem's history to read?

### Key decisions that need your eyes (12 min)

**Appeal messages notify on every message** ([US2 scenario 3](spec.md#user-story-2---the-appeal-lifecycle-is-fully-voiced-priority-p1), [EC-002](spec.md#edge-cases))

Each `PostAppealMessage` emails the other party — a chat-like cadence was explicitly accepted.
- Question: in a heated appeal with 10 rapid messages, is 10 emails acceptable, or does this want a debounce/digest before it ships rather than as the deferred [OQ-001](spec.md#open-questions)?

**Dual-fire on GrantReopenToReview** ([FR-006](spec.md#functional-requirements), [EC-001](spec.md#edge-cases))

Resolving an appeal as reopen-to-review fires *two* events in one transaction (applicant + reviewers).
- Question: the two rows share `VersionHistoryId` and differ only by `EventType` for idempotency — is that distinction robust enough, or would you want a more explicit anchor? See [contract](contracts/notification-events.md#dual-fire-contract-event-5--6).

**Adding a `VersionHistory` row to convenio generation** ([FR-010](spec.md#functional-requirements), [R-007](research.md#r-007--agreementgenerated-audit-row-placement))

Generation currently writes no audit row; this spec adds one so the idempotency anchor is uniform — a behavior change to a non-notification path.
- Question: is silently making convenio generation auditable a welcome side effect, or a scope creep that should be its own change? It is the only edit outside the notification subsystem.

**Reusing the group-overlap reviewer set for signing events** ([R-006](research.md#r-006--signing-stage-reviewer-recipients))

Signing-stage reviewer recipients reuse the same group-overlap query the signing inbox uses.
- Question: should the reviewer who is *actively driving* a given application's signing be prioritized over the whole group, or is notifying the full overlapping group correct?

### Areas where I'm less certain (5 min)

- [FR-013a](spec.md#functional-requirements) / [EC-011](spec.md#edge-cases) (actor exclusion): I added this during spec review to stop an actor-who-is-also-an-admin from emailing themselves. It generalizes the existing applicant-exclusion in the resolver, but I have not confirmed there is no existing event that *intends* to notify the actor — the assumption is "none do today." Worth a sanity check against the shipped 7 events.
- [R-005](research.md#r-005--two-phase-save-and-the-versionhistoryid-anchor) (two-phase save): the plan says "mirror `ReviewService.SendBackAsync`," but the exact ordering of VersionHistory-id assignment vs. enqueue in the reference code wasn't transcribed line-for-line. If the canonical pattern does a single `SaveChangesAsync`, [T012/T019-T021/T028-T030](tasks.md#phase-3-user-story-1--applicant-response-reaches-the-reviewer-priority-p1-) need to follow whatever the reference actually does — implementers must read it, not assume.
- [SignedUploadRejectedApplicant](contracts/notification-events.md#per-event-payload-contract) PII cue: "convey changes-required without verbatim reviewer commentary" ([NFR-003](spec.md#non-functional-requirements)) is a judgment call in the partial copy — there is no automated test for "no PII," only the reviewer's eye on [T027](tasks.md#phase-5-user-story-3--convenio-signing-ceremony-fully-voiced-priority-p1).

### Risks and open questions (5 min)

- The three user stories all edit three shared files (`NotificationEvent.cs`, `NotificationTemplateBindings.cs`, `NotificationRecipientResolver.cs`). If they are parallelized, is the merge-coordination note in [tasks.md](tasks.md#path-conventions) enough, or should the enum/binding scaffolding for all 12 events be a single foundational task to avoid churn?
- The inherited [OQ-011](spec.md#open-questions) limitation (a demoted-admin actor won't be matched as a participating admin) now applies to 12 more events. Is shipping that known gap again acceptable, or does its blast radius now justify fixing it?
- E2E for these flows assumes Page Objects for the applicant-response, appeal, and signing UIs exist or can be extended ([tasks T014/T023/T032](tasks.md#phase-4-user-story-2--appeal-lifecycle-fully-voiced-priority-p1)). If those POMs don't exist yet, the E2E effort is larger than the task list implies — worth confirming before estimating.

---
*Full context in linked [spec](spec.md), [plan](plan.md), [tasks](tasks.md), and [contracts](contracts/notification-events.md).*
