# Review Brief: Email Notifications System (021)

**Spec:** specs/021-email-notifications/spec.md
**Generated:** 2026-05-11

> Reviewer's guide to scope and key decisions. See full spec for details.

---

## Feature Overview

The platform today has zero out-of-band notifications. This spec ships the first email subsystem, covering five workflow events — application submission, send-back to applicant, resubmission, final approval, and final rejection. Emails route to a containerized SMTP-capture sidecar in local development, to Mailgun in non-local environments, and degrade gracefully to a no-op sender when provider credentials are missing in non-production. Admins are notified only when they have *explicitly acted* on an application; passive views do not pull them in. A fail-closed allowlist filter prevents staging deployments from emailing real users. All templates carry the spec-019 Programa Semilla / Sistema de Banca para el Desarrollo identity, render in es-CR Spanish, and ship with no inline images to preserve email-client compatibility.

## Scope Boundaries

- **In scope:** 5 workflow events; outbox + worker architecture; provider abstraction (Mailtrap-sidecar local / Mailgun prod / NoOp fallback); spec-019 branded HTML+text templates; explicit-action admin-participation predicate; non-prod allowlist guard fail-closed; idempotency + retry; dacpac schema (two new tables); SMTP-capture sidecar wired into Aspire dev + AspireFixture E2E; replacement of spec 019's `EmailTemplateSenderTests.Assert.Ignore` placeholder.
- **Out of scope:** In-app notifications / SignalR / push / SMS; stage-granular events beyond the 5; signing-stage events; user-facing preferences UI; digests / batching; multi-language templates; Mailgun bounce-webhook ingestion; reply-to-notification handling.
- **Why these boundaries:** Spec scope was deliberately minimized to the highest-value v1 cut per the seed's §16 strong-stance recommendation. The deferred items are not lost — they live as multi-spec open threads dating back to brainstorms #08 and #11.

## Critical Decisions

### Outbox + worker (not inline send, not domain-event dispatcher)
- **Choice:** Transactional outbox row written in the same EF transaction as the workflow state change; a hosted `BackgroundService` poller drains it.
- **Trade-off:** Adds two new tables + one hosted service to operate. The alternative — inline send — would couple workflow latency to Mailgun availability and weaken the audit trail.
- **Feedback:** Is the operational complexity of an outbox worth the resilience + audit gains for a v1 with low expected volume? Recommended: yes, because send-back / resubmission cycles are core to the workflow and notification reliability is the load-bearing user-felt promise.

### Admin participation = explicit prior action only
- **Choice:** An admin receives a notification on an application only if their user-id appears in the `Application.VersionHistory` actor column or in an existing audit entry tied to that application id. Passive views do not qualify.
- **Trade-off:** Surgical and explainable, but a brand-new admin who never touched an application receives nothing for it. The alternative — group-overlap or current-assignment — would surface admins who never explicitly acted.
- **Feedback:** Is "explicit action" the right precision, or should current-assignment also qualify? Recommendation: explicit-action only, per seed §13 Q1 recommendation.

### SMTP-capture sidecar as local default (not real Mailtrap)
- **Choice:** A containerized sidecar (smtp4dev or MailHog, planning-pin) is the default Local provider, wired into AppHost alongside SQL Server. Real Mailtrap (cloud) remains an opt-in override via config.
- **Trade-off:** Adds one container to dev startup. Eliminates third-party network dependency for local dev + CI; gives E2E tests a captureable inbox; replaces the dangling spec-019 `Assert.Ignore` placeholder with a real assertion path.
- **Feedback:** Is local-first sidecar the right default, or should the team standardize on real Mailtrap for cross-device sharing of preview URLs?

### Non-prod allowlist guard fail-closed
- **Choice:** Outside Production, an `IEmailSender` decorator drops every recipient not in `Notifications:NonProdAllowlist` and records `BlockedByAllowlist` in the audit table. Empty allowlist on a staging deployment = zero real users emailed.
- **Trade-off:** A misconfigured staging environment silently drops all outbound mail (but the audit row records every drop, making the misconfiguration visible in seconds).
- **Feedback:** Is fail-closed (zero emails when allowlist is empty) the right safety posture, vs. fail-open with a `[STAGING]` subject prefix on every email? Recommendation: fail-closed for a fintech.

## Areas of Potential Disagreement

### Splitting `APPLICATION_SUBMITTED` into two enum values
- **Decision:** Outbox event-type enum splits `APPLICATION_SUBMITTED` into `APPLICATION_SUBMITTED_REVIEWER` and `APPLICATION_SUBMITTED_APPLICANT` so each outbox row carries one template variant.
- **Why this might be controversial:** Two enum values for "one workflow event" can read as overengineering at first glance.
- **Alternative view:** Keep `APPLICATION_SUBMITTED` as a single enum value and have the resolver fan out to two template variants in memory.
- **Seeking input on:** OQ-006. The split keeps the idempotency unique index clean (one row per template variant per recipient); the unified approach simplifies the enum but pushes complexity into the resolver. Recommendation: split.

### Hardcoded es-CR strings, no i18n key system
- **Decision:** Templates contain literal es-CR Spanish strings. No i18n key system. No English fallback.
- **Why this might be controversial:** Most production email systems use an i18n key registry from day one.
- **Alternative view:** Introduce a key system now (even if only es-CR is populated) to future-proof multi-language support.
- **Seeking input on:** Spec 012 hard-pinned the platform to es-CR. Adding an i18n system pre-emptively violates constitution §VI (no speculative abstractions). Recommendation: hardcode now, refactor when a second language lands.

### MailKit as a new managed NuGet dependency
- **Decision:** `MailKit` (one new managed NuGet) for the SMTP path. Mailgun path uses raw `HttpClient` with no Mailgun SDK.
- **Why this might be controversial:** CLAUDE.md states "New managed (NuGet) dependencies require spec approval. Default posture: reuse what is vendored." Approval is embedded in this spec via FR-014.
- **Alternative view:** Use the deprecated `System.Net.Mail.SmtpClient` to avoid the dependency.
- **Seeking input on:** Microsoft's documentation explicitly recommends against `System.Net.Mail.SmtpClient` for new code. MailKit is the de-facto choice. Recommendation: approve `MailKit`.

### Worker polls instead of subscribing to a queue
- **Decision:** Single hosted `BackgroundService` polls the outbox on a configurable interval (default 5s). No SignalR, no message bus.
- **Why this might be controversial:** A polling worker reads as low-tech.
- **Alternative view:** Use SQL Server Service Broker or an in-process queue for push-style dispatch.
- **Seeking input on:** Volume is low (workflow events at human cadence, not machine). Polling is simpler to operate. Push-style would optimize for a scale the platform does not have. Recommendation: poll.

## Naming Decisions

| Item | Name | Context |
|---|---|---|
| Outbox table | `NotificationOutbox` | dacpac-defined |
| Delivery audit table | `NotificationDelivery` | dacpac-defined; carries the idempotency unique index |
| Event enum | `NotificationEvent` | `APPLICATION_SUBMITTED_REVIEWER`, `APPLICATION_SUBMITTED_APPLICANT`, `RETURNED_TO_APPLICANT`, `RESUBMITTED_BY_APPLICANT`, `APPLICATION_APPROVED`, `APPLICATION_REJECTED` |
| Provider abstraction | `IEmailSender` | three impls: `MailtrapSmtpEmailSender`, `MailgunHttpEmailSender`, `NoOpEmailSender` |
| Recipient resolver | `INotificationRecipientResolver` | Application-layer service |
| Allowlist decorator | `RecipientAllowlistFilter` | wraps `IEmailSender` in non-Production |
| Worker | `NotificationOutboxWorker` (hosted `BackgroundService`) | single instance per Web replica |
| Layout partial | `_EmailLayout.cshtml` | spec-019 wordmark, signature, footer, CTA styles |
| E2E capture client | `MailCaptureClient` | extension of `AspireFixture` |
| Sender display | `Programa Semilla / Sistema de Banca para el Desarrollo` | pinned by spec 019; configurable via `Notifications:Sender:Name` |

## Open Questions

- [ ] OQ-001 — Does Mailgun's transactional-email ToS require a `List-Unsubscribe` header or visible unsubscribe link? Decide before non-Local provisioning.
- [ ] OQ-002 — SMTP-capture sidecar: smtp4dev (.NET-native, lighter) vs MailHog (Go, broadly used)?
- [ ] OQ-003 — Real Mailtrap stays an opt-in dev override (sidecar default). Confirm during planning.
- [ ] OQ-004 — Production sender email: `no-reply@programa-semilla.cr` recommended; confirm with ops.
- [ ] OQ-005 — MailKit license posture: v3 (MIT) vs v4 (commercial)?
- [ ] OQ-006 — Two enum values for `APPLICATION_SUBMITTED` vs one with two template variants?
- [ ] OQ-007 — Confirm `Application.Folio` field exists and is populated by `Submit()`.
- [ ] OQ-008 — `NotificationOutbox` retention: 90 days for `Done`, 1 year for `DeadLetter` recommended.
- [ ] OQ-009 — Future multi-replica worker: correctness is provable today (FR-004 + FR-020); throughput tuning deferred.
- [ ] OQ-010 — Brand-grep gate scope: source-`.cshtml` layer (recommended) vs render-time scan.

## Risk Areas

| Risk | Impact | Mitigation |
|---|---|---|
| Staging deployment spams real users with production data | High | Fail-closed allowlist guard (FR-017..FR-019); empty allowlist on non-Production = zero deliveries to real recipients; every drop audited in `NotificationDelivery` |
| Mailgun outage swallows workflow notifications | High | Outbox + retry with exponential backoff (1s/5s/30s × 3 attempts); transient vs permanent failure classification; dead-letter rows visible in audit |
| Email contains internal-only reviewer commentary | Medium | NFR-003 forbids verbatim commentary; emails link to in-app surfaces where access control enforces the rest |
| Duplicate sends on worker restart or retry | Medium | Idempotency unique index on `(EventType, ApplicationId, VersionHistoryId, RecipientUserId)`; worker no-ops on existing-`Sent`-row check before contacting provider |
| EF migrations accidentally introduced | Medium | NFR-005 + SC-008 + CI grep gate over `**/Migrations/**` |
| "Capital Semilla" / "Forge" string leaks into new templates | Medium | Spec-019 brand-grep gate (T030) stays green; FR-027 binds it |
| Future spec adds a sixth event and breaks dedup | Low | Idempotency key composition is event-typed; new events allocate fresh `EventType` enum entries with no risk to existing rows |
| `MailKit` license shifts to commercial v4 | Low | OQ-005 pins version; v3 (MIT) is the fallback if v4 commercial is rejected |

---
*Share with reviewers before implementation.*
