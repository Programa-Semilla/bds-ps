---
name: brainstorm-email-notifications
description: First out-of-band notification subsystem — email-only, outbox+worker, Mailtrap-sidecar local / Mailgun prod, spec-019 brand. 5-event minimal v1. Replaces spec 019's Assert.Ignore placeholder.
metadata:
  type: brainstorm
---

# Brainstorm: Email Notifications System

**Date:** 2026-05-11
**Status:** spec-created
**Spec:** specs/021-email-notifications/

## Problem Framing

The platform today has **zero out-of-band notifications**. Applicants poll the UI to learn status. Reviewers don't know new work arrived. Admins who once participated lose all visibility once they navigate away. Spec 019 ("Programa Semilla brand pivot") left an explicit `Assert.Ignore` placeholder in `tests/FundingPlatform.Tests.E2E/Brand/EmailTemplateSenderTests.cs` that forwarded the email-subsystem contract to a later spec — this is that spec.

Constraints from the seed (`brainstorm/seeds/email-notifications-seed.md`):
- Email only (no SMS / push / in-app).
- Mailtrap locally, Mailgun everywhere else.
- Match the spec-019 Programa Semilla brand language.
- Use spec number **021** (020 reserved for parallel work).

## Approaches Considered

### A: Outbox + background worker — **chosen**
- Transactional outbox row written in the SAME EF transaction as the workflow state change.
- Hosted `BackgroundService` polls outbox, resolves recipients, renders templates, sends via `IEmailSender`, writes `NotificationDelivery` audit row.
- Idempotency: unique index on `(EventType, ApplicationId, VersionHistoryId, RecipientUserId)`.
- Retry: transient 5xx with `(1s, 5s, 30s)` backoff, dead-letter on permanent failure.
- **Pros:** Workflow latency decoupled from Mailgun availability; centralized retry; complete audit trail; correctness under future multi-replica via row-claim + idempotency.
- **Cons:** Two new tables + one hosted service; slightly more dacpac work.

### B: Inline send inside transition handlers
- `SubmitApplication`, `SendBack`, `Approve`, etc. each call `IEmailSender` directly after EF SaveChanges.
- **Pros:** Quick to ship; minimal new infra.
- **Cons:** Workflow latency includes SMTP RTT; Mailgun outage = transition failure; bespoke per-handler retry; weak audit; no idempotency story under retry.
- **Verdict:** Rejected — cheap to ship, expensive to operate.

### C: Domain-event dispatcher (`IDomainEventDispatcher`)
- Domain events (`ApplicationSubmittedEvent`, etc.) raised by aggregates; an `INotificationHandler<>` subscribed to each.
- **Pros:** Cleanest separation; extensible for future channels (SMS / in-app); aligns with constitution §II (Rich Domain Model).
- **Cons:** Repo has no domain-event dispatcher today; constitution §VI forbids speculative abstractions. Premature for v1.
- **Verdict:** Rejected for v1; revisit if a second channel lands.

## Decision

**Approach A (Outbox + worker).** Spec created at `specs/021-email-notifications/`. The architecture decision is documented in `specs/021-email-notifications/implementation-notes.md` along with rationale for the rejected alternatives.

Key locked decisions from clarifying-question pass:

1. **Brand source = spec 019.** The seed's "Agreement generation email" premise was a misstatement (no email subsystem existed). Templates inherit spec 019's text-only wordmark, no inline `<img>`, no Capital Semilla / Forge leakage.
2. **Event catalog v1 = 5 events.** `APPLICATION_SUBMITTED`, `RETURNED_TO_APPLICANT`, `RESUBMITTED_BY_APPLICANT`, `APPLICATION_APPROVED`, `APPLICATION_REJECTED`. Stage-granular events (`STAGE_APPROVED`, `REVIEWER_ASSIGNED`, `COMMENT_ADDED`) deferred.
3. **Admin participation = explicit prior action only.** Resolver reads `Application.VersionHistory` + existing audit; no new audit infrastructure; passive views do not qualify.
4. **Applicant gets a submission confirmation.** Two recipient buckets on `APPLICATION_SUBMITTED` (reviewer-variant + applicant-variant), two enum values to keep idempotency keys clean.
5. **Non-prod safety = fail-closed allowlist.** `RecipientAllowlistFilter` decorator wraps `IEmailSender` outside Production; empty allowlist = zero real users emailed; every drop audited.
6. **Architecture = A + SMTP-capture sidecar.** smtp4dev or MailHog (planning-pin) wired into `AppHost.cs`; `AspireFixture` exposes `MailCaptureClient` for E2E; replaces spec-019's `Assert.Ignore`.

## Open Threads

- OQ-001 — Mailgun ToS unsubscribe footer: `List-Unsubscribe` header / static `mailto:soporte@…` line vs nothing. Pin during planning with Mailgun account owner.
- OQ-002 — SMTP-capture sidecar choice: smtp4dev vs MailHog. Pin during planning.
- OQ-003 — Real Mailtrap remains an opt-in dev override; sidecar is the default. Confirm during planning.
- OQ-004 — Production sender email: `no-reply@programa-semilla.cr` recommended. Pin with ops.
- OQ-005 — `MailKit` license posture: v3 (MIT) vs v4 (commercial). Confirm at planning.
- OQ-006 — `APPLICATION_SUBMITTED` enum split (two values vs one with two template variants). Recommended split for clean idempotency. Confirm during planning.
- OQ-007 — Confirm `Application.Folio` field exists and is populated by `Submit()`. Pin against spec 001 / data-model.
- OQ-008 — `NotificationOutbox` retention: 90 days for `Done`, 1 year for `DeadLetter` recommended.
- OQ-009 — Future multi-replica worker scaling — correctness covered today (FR-004 + FR-020); throughput tuning deferred.
- OQ-010 — Brand-grep gate scope: source-`.cshtml` layer (recommended) vs render-time scan.
- Forward — In-app notifications / SignalR / bell-icon inbox: remains open multi-spec thread from #08 / #11; not closed by spec 021.
- Forward — Signing-stage events (`AGREEMENT_GENERATED`, `SIGNED_PDF_UPLOADED`): out of scope for 021; eligible for a follow-up spec once 005 / 006 traffic patterns are observed.
- Forward — Stage-granular events (`STAGE_APPROVED`, `REVIEWER_ASSIGNED`, `COMMENT_ADDED`): out of scope for 021; eligible for v2 if reviewer churn proves a real signal.
- Forward — User-facing notification-preferences UI / opt-out flow: deferred. Mailgun ToS verdict (OQ-001) may force a static unsubscribe-mailto footer in the meantime.
- Forward — Mailgun bounce-webhook ingestion + suppression-list sync: deferred until Mailgun delivery telemetry justifies the loop.

---

## Revisit: 2026-05-27 — Post-Resolution Notifications (spec 028)

### Updated Problem Framing

Spec 021 shipped, but its event catalog stopped at `Resolved`. A reported bug surfaced the gap: a reviewer was **never notified when the applicant accepted the reviewer's resolution**. Tracing the applicant↔reviewer lifecycle showed the system goes **completely dark after Finalize** — applicant-response, the entire appeal thread, and the convenio signing ceremony send zero email. This revisit closes the two "Forward" threads above (signing-stage events; appeal-adjacent events) by lifting the spec-021 out-of-scope items into scope.

### New Approaches Considered

- **Fold into spec 021 (US additions + standalone plan-*.md)** vs **new spec 028 dir.** Chose **new spec 028** — the increment is 12 events + an audit-row change, large enough to warrant its own dir; keeps shipped 021 frozen and satisfies the standard speckit flow. (Spec 021's own US9 `WithdrawnByApplicant` had been folded in previously; this one is big enough to stand alone.)
- **Notify every actor incl. self-confirmations** vs **counterparty-only.** Chose **counterparty-only** — matches the existing `SendBack`/`Approve`/`Reject` precedent (no self-confirm); only the original `Submit` confirms to the applicant. Leaner, consistent.
- **Idempotency anchor:** confirmed 11 of 12 triggers already write a `VersionHistory` row in-transaction → existing `(EventType, ApplicationId, VersionHistoryId, RecipientUserId)` index reused unchanged. The 12th (convenio generation) gains an `AgreementGenerated` VersionHistory row (via `Application.AddVersionHistory`, §II) — uniform anchor + a side benefit: generation becomes audited.

### Updated Decision

**Spec created at `specs/028-post-resolution-notifications/`.** 12 new `NotificationEvent` values, 3 user stories (US1 applicant-response→reviewer = the bug fix; US2 appeal lifecycle = 5 events incl. bidirectional messages + GrantReopenToReview dual-fire; US3 signing ceremony = 6 events). Counterparty-only; reuses all spec-021 infra; one partial pair per event (24 files); es-CR; **schema unchanged**. Spec-review gate: **SOUND (5/5)**. Two inline fixes: §II domain-method audit append (FR-010); actor-self-exclusion (FR-013a / EC-011).

User-confirmed scoping: appeal threads notify on **every** message (chat-cadence accepted); signed-upload notifies on **upload + replace + withdraw** (all three).

### Open Threads

- Still forward (unchanged by 028): in-app/SignalR/bell-inbox; remaining stage-granular events (`STAGE_APPROVED`, `MOVED_TO_NEXT_STAGE`, `REVIEWER_ASSIGNED`, `COMMENT_ADDED` on non-appeal surfaces); notification-preferences UI; Mailgun bounce-webhook ingestion; outbox/delivery retention cleanup job.
- Inherited limitation: spec-021 OQ-011 participating-admin role-change predicate applies to all 12 new events; fixing it is out of scope for 028.
- 028 OQ-001 — appeal-message email cadence at scale (debounce/digest) if high-volume threads prove noisy. Deferred.
- Planning-pin — exact `/Applications/{id}/FundingAgreement/` sub-route per applicant CTA; whether `APPEAL_MESSAGE_*` carries a snippet or a bare "new message" cue (NFR-003 leans cue+CTA).
