# Implementation Notes: Email Notifications System (spec 021)

> Captures the technical-decision context that emerged during brainstorming. The
> spec stays focused on WHAT and WHY. This file is the HOW-context companion —
> alternative approaches considered, trade-offs, technology choices, and the
> reasoning behind defaults. Future implementers should read this alongside
> `spec.md`.

## Design Decisions

### Decision: Outbox + worker (option A), not inline-send and not domain-events

- **Chose**: Transactional outbox written in the same EF transaction as the
  workflow state change; hosted `BackgroundService` polls and dispatches.
- **Rationale**: Workflow correctness must not depend on Mailgun being up;
  retries must be centralized; the audit story is non-negotiable for a fintech
  workflow. Constitution §VI ("simplicity") is preserved because the outbox
  serves a *current* reliability need (the team has explicit retry / dedupe
  requirements from the seed) — not a speculative one.
- **Rejected — inline send inside transition handlers**: Couples workflow
  latency to SMTP RTT, ties retries to bespoke per-handler code, weakens audit.
  Cheap to ship; expensive to operate.
- **Rejected — event-driven via `IDomainEventDispatcher`**: Would require
  introducing a new cross-cutting abstraction (`IDomainEventDispatcher`,
  `IDomainEventHandler<>`) that the repo does not have today. Constitution §VI
  forbids abstractions for speculative needs. v2 can revisit if a second
  channel (in-app / SMS) lands.

### Decision: Outbox event-type split for `APPLICATION_SUBMITTED`

- **Chose**: Two distinct enum values — `APPLICATION_SUBMITTED_REVIEWER` and
  `APPLICATION_SUBMITTED_APPLICANT` — produced by the same `Submit()`
  transaction, each carrying its own outbox row.
- **Rationale**: Keeps the idempotency unique index
  `(EventType, ApplicationId, VersionHistoryId, RecipientUserId)` clean: one
  row per template variant per recipient. Recipient resolver is simpler: one
  bucket per event. Alternative — one event with two template variants —
  collapses the index logic and requires resolver code to fan out variants in
  memory, which is error-prone under retry. Open question OQ-006 in spec
  ratifies this during planning, but the recommended default is the split.

### Decision: SMTP-capture sidecar (smtp4dev / MailHog) as Local default, real Mailtrap as opt-in

- **Chose**: A containerized sidecar starts alongside SQL Server on
  `dotnet run --project AppHost`. Web wires its SMTP endpoint from
  Aspire-resolved config.
- **Rationale**: Spec 019's FINDING-4 explicitly noted the missing
  in-process IServiceProvider seam on `AspireFixture` blocks SMTP-capture
  E2E. A sidecar with an HTTP capture API is the cleanest fix: production-shape
  SMTP path, in-test inspection. Real Mailtrap (cloud) becomes a dev override
  via config; the default stays self-contained so CI / offline dev never hits
  a third-party rate limit. Open question OQ-002 pins the exact sidecar choice
  during planning; the recommendation is `smtp4dev` because it is .NET-native
  and lighter than MailHog (Go).

### Decision: `MailKit` for Local SMTP, raw `HttpClient` for Mailgun

- **Chose**: `MailKit` (one new managed NuGet — approval embedded in spec FR-014)
  for the SMTP path; raw `HttpClient` against Mailgun's HTTP API for the
  Mailgun path (no Mailgun SDK).
- **Rationale**: .NET's `System.Net.Mail.SmtpClient` is officially deprecated
  and documented unsuitable for new code. `MailKit` is the de-facto SMTP
  library in the .NET ecosystem. Mailgun's REST API is simple enough that a
  Mailgun-specific NuGet would add drift risk without saving meaningful code.
- **Open question OQ-005** pins the `MailKit` version (v3 MIT vs v4 commercial)
  during planning.

### Decision: Non-prod allowlist is a decorator, not a config-time switch

- **Chose**: A `RecipientAllowlistFilter` decorator wraps the active
  `IEmailSender` in non-Production. Recipients failing the filter are dropped
  and recorded as `NotificationDelivery.Status=BlockedByAllowlist`.
- **Rationale**: Audit trail of *what would have been sent* is the killer
  feature: when a staging deployment is suspected of leaking real users, the
  `NotificationDelivery` table answers the question definitively. A
  config-time switch (e.g., "only send to allowlist") would silently drop
  recipients without leaving a paper trail.
- **Alternative — catch-all redirect (rewrite all recipients to
  `staging-mail@…`)** was rejected: it produces test mail that is hard to
  partition by intended recipient and makes diff'ing recipient sets brittle.

### Decision: Idempotency anchored on `VersionHistoryId`, not a UUID

- **Chose**: Unique index on
  `(EventType, ApplicationId, VersionHistoryId, RecipientUserId)`. Each
  workflow state transition produces a `VersionHistory` row in the same
  transaction; that ID becomes the natural anchor.
- **Rationale**: Avoids storing a separate idempotency key column when the
  database already produces a per-transaction unique identifier. Aligns with
  spec 002's existing `VersionHistory` audit shape.
- **Trade-off**: If a future spec splits a single state transition into
  multiple `VersionHistory` rows, the resolver may need a more granular anchor.
  Captured here so a future implementer can re-anchor without re-deriving the
  decision.

### Decision: Schema in dacpac, not EF migrations

- **Chose**: `NotificationOutbox` and `NotificationDelivery` defined as
  `.sql` files in `FundingPlatform.Database`. EF Core Code-First maps the
  entities against the dacpac shape.
- **Rationale**: Constitution §IV non-negotiable. SC-008 enforces zero new
  migrations via a CI grep gate.

### Decision: Templates in Razor `.cshtml`, not a templating-engine NuGet

- **Chose**: Razor partials under
  `src/FundingPlatform.Web/Views/Emails/` (or `Areas/Notifications/Views/`
  if scoped). Body partials render against typed view-models; layout is shared
  via `_EmailLayout.cshtml`.
- **Rationale**: Razor is already in the stack. Adding a templating engine
  (RazorLight, Fluid, Liquid) would be a managed-dep violation per CLAUDE.md
  without serving a current need. The spec-019 brand-grep gate (T030) already
  scans Razor sources; keeping templates as `.cshtml` means the gate stays
  source-time (OQ-010 recommendation).

### Decision: No List-Unsubscribe header / suppression list in v1

- **Chose**: Static no-reply sender; v1 ships without `List-Unsubscribe`. If
  Mailgun ToS requires it (OQ-001), the v1 footer carries a static
  `mailto:soporte@…` line as the unsubscribe path — manual ops, no automated
  suppression list.
- **Rationale**: Transactional workflow mail to verified users is generally
  exempt from one-click-unsubscribe under Mailgun's transactional-email
  category, but the account owner must confirm. Suppression-list ingestion
  via webhook is explicitly out of scope.

### Decision: es-CR Spanish hardcoded, no i18n key system

- **Chose**: Template strings are in es-CR directly in the `.cshtml` files.
- **Rationale**: Spec 012 hard-pinned the culture. CLAUDE.md says
  "Translation/localization is in scope (spec 012)." No multi-language v1
  variant exists; adding an i18n key system now would be premature per
  constitution §VI.

## Open Questions Carried to Planning

See `spec.md` §Open Questions for the full list (OQ-001..OQ-010). Recommended
defaults for each are stated in the spec; planning ratifies or overrides.

## E2E Architecture Note

Spec 019 left an explicit forwarding contract in
`tests/FundingPlatform.Tests.E2E/Brand/EmailTemplateSenderTests.cs`:

> Until the email subsystem lands, this test is INTENTIONALLY a static
> `Assert.Ignore` — the brand-grep gate (T030) is the standing guard for
> stale "Capital Semilla" / "Forge" strings in any future template, so the
> FR-006 / NFR-005 regression cannot sneak past unnoticed even with the
> SMTP-capture body inactive.

This spec's FR-031 + FR-032 close that contract: `AspireFixture` gets a
`MailCaptureClient` that drains the sidecar's HTTP API, and the
`Assert.Ignore` is replaced with real `[Test]` cases per event variant. The
brand-grep gate remains a belt-and-suspenders source-time check.
