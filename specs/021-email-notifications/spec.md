# Feature Specification: Email Notifications System

**Feature Branch**: `feature/notifications`
**Created**: 2026-05-11
**Status**: Draft
**Input**: User description: Introduce the first email-notification subsystem in the FundingPlatform. Today, workflow events (`Submit`, `SendBack`, `Resubmit`, `Approve`, `Reject`) produce no out-of-band signal — applicants poll the UI for status, reviewers don't know new work has arrived, and admins who once participated in an application lose all visibility once they navigate away. Spec 019 ("Programa Semilla brand pivot") shipped a placeholder E2E test `tests/FundingPlatform.Tests.E2E/Brand/EmailTemplateSenderTests.cs` with `Assert.Ignore` that explicitly forwarded the email-subsystem contract to a later spec; this is that spec. Recipients are determined by three buckets — assigned-stage reviewers, the applicant, and admins who have *explicitly acted* on the application (no passive-view spillover) — with `applicant > reviewer > admin` bucket priority deciding the single template variant when a user qualifies via multiple routes. Outbox + background-worker architecture (transactional outbox written in the same DB transaction as the workflow state change; hosted `BackgroundService` poller; idempotency via unique index; transient-retry with backoff; permanent-failure dead-letter). Provider abstraction routes outbound mail to a containerized SMTP-capture sidecar (smtp4dev / MailHog) in Local — wired into the Aspire dev orchestration alongside SQL Server — Mailgun HTTP API outside Local, and a `NoOpEmailSender` fallback that fail-fasts on boot in Production when provider config is missing but warns-and-continues in non-Production. A `RecipientAllowlistFilter` decorator wraps the sender in every environment other than Production: non-allowlisted recipients are dropped and recorded as `BlockedByAllowlist`, so an empty allowlist on a staging environment yields zero real users emailed (fail-closed). All templates are es-CR Spanish (constitution + spec 012 hard-pinned culture), Razor-rendered with HTML + text fallback under one shared `_EmailLayout.cshtml` carrying the spec-019 wordmark text-only (no inline `<img>` per spec 019 NFR-005). Schema lives in the `FundingPlatform.Database` dacpac (two new tables: `NotificationOutbox`, `NotificationDelivery`) — zero EF migrations per constitution §IV. The deferred-counterpart placeholder test `EmailTemplateSenderTests.Assert.Ignore` is removed and replaced with real send/capture assertions per event variant, driven through an `AspireFixture` extension that exposes a `MailCaptureClient` against the sidecar. v1 event catalog is intentionally minimal — five workflow events (`APPLICATION_SUBMITTED`, `RETURNED_TO_APPLICANT`, `RESUBMITTED_BY_APPLICANT`, `APPLICATION_APPROVED`, `APPLICATION_REJECTED`); stage-granular and signing-stage events (`STAGE_APPROVED`, `REVIEWER_ASSIGNED`, `AGREEMENT_GENERATED`, `SIGNED_PDF_UPLOADED`, `COMMENT_ADDED`) are deliberately out of scope and tracked as multi-spec open threads from #08 / #11. In-app notifications, SignalR, push, SMS, digests, multilingual templates, user-facing notification-preferences UI, and Mailgun bounce-webhook ingestion are also explicitly out of scope. Spec number 021 per `brainstorm/seeds/email-notifications-seed.md` (020 reserved for parallel work).

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Applicant submits and the workflow speaks back (Priority: P1)

A logged-in applicant completes their application and presses Submit. Within seconds, the applicant receives an email confirming the submission and providing a deep link back to the application's read-only detail view. In parallel, every reviewer currently assigned to the intake group receives an email announcing a new application to review with a deep link to the reviewer queue. Any admin who has *explicitly acted* on this same application in the past (e.g., from a prior life as a reviewer who later got promoted, or from a manual admin-edit action) also receives the reviewer-flavored notification. All emails carry the Programa Semilla / Sistema de Banca para el Desarrollo identity from spec 019: text-only wordmark, no inline images, es-CR Spanish.

**Why this priority**: This is the single most visible deliverable — the closed-loop "I submitted, the system acknowledged, the people who can act know about it" promise. If only ONE event were ever implemented, this is the one that delivers the most user-felt value (applicant relief + reviewer awareness). P1.

**Independent Test**: Run an E2E that signs in as an applicant, submits an application, and asserts via the SMTP-capture sidecar that (a) the applicant inbox received exactly one email with the applicant-variant subject `Recibimos tu solicitud — {Folio}` and a deep link to `/Applications/Details/{id}`, AND (b) each reviewer of the intake group received exactly one email with the reviewer-variant subject `Nueva solicitud para revisar: {ApplicantName}` and a deep link to `/Reviewer/Applications/Details/{id}`. No duplicates. Sender display reads "Programa Semilla / Sistema de Banca para el Desarrollo".

**Acceptance Scenarios**:

1. **Given** a draft application with all required fields completed and a reviewer group assigned to the intake stage, **When** the applicant presses Submit, **Then** the SMTP sidecar captures one applicant-variant email AND one reviewer-variant email per reviewer in the assigned group, all with the spec-019 sender display and signature block.
2. **Given** a submission in the same conditions, **When** the email bodies are inspected, **Then** no email contains an inline `<img>` tag, no email contains the strings "Capital Semilla" or "Forge", and every body is rendered in es-CR Spanish.
3. **Given** an admin user who performed an explicit action on this application in a previous workflow round (e.g., an earlier resubmission cycle), **When** the new submission fires, **Then** that admin receives the reviewer-variant email; another admin user with no participation history receives nothing.
4. **Given** a successful submission, **When** the `NotificationDelivery` table is queried, **Then** one row per intended recipient exists with `Status=Sent`, and the unique-index columns `(EventType, ApplicationId, VersionHistoryId, RecipientUserId)` are populated.

---

### User Story 2 - Reviewer sends back, applicant gets called to action (Priority: P1)

A reviewer working an application identifies issues and sends the application back to the applicant. Within seconds, the applicant receives an email titled `Acción requerida: actualiza tu solicitud — {Folio}` with a deep link to their application detail. Reviewers receive no email on this transition (it's the applicant's turn). Participating admins receive a copy.

**Why this priority**: This is the workflow's primary feedback loop. Without it, an applicant has to poll the UI repeatedly to discover whether their application has come back to them. P1 because it directly determines how quickly applicants can resume work on a returned application.

**Independent Test**: Run an E2E that submits an application, signs in as a reviewer, sends it back with a reason, and asserts the SMTP sidecar captures exactly one applicant-variant email — and zero reviewer-variant emails — with the `RETURNED_TO_APPLICANT` subject and an `/Applications/Details/{id}` deep link.

**Acceptance Scenarios**:

1. **Given** a submitted application with at least one reviewer-in-group having opened it, **When** a reviewer sends the application back, **Then** the applicant receives the `Acción requerida: actualiza tu solicitud — {Folio}` email and reviewers receive no email for this event.
2. **Given** the same send-back, **When** the participating-admin predicate is evaluated, **Then** any admin who has explicitly acted on this application history receives the same email body as the applicant variant (or a participating-admin variant of the send-back template).
3. **Given** the applicant's email has been changed by an admin between submission and send-back, **When** the worker dispatches, **Then** the email is delivered to the applicant's *current* email address — not the address recorded at submission time.

---

### User Story 3 - Applicant resubmits, reviewers re-engaged, no duplicates (Priority: P1)

The applicant addresses the send-back feedback and presses Resubmit. Within seconds, reviewers of the current stage group receive an email titled `Solicitud reenviada para revisión: {ApplicantName}` with a deep link to the reviewer detail surface. The applicant receives no email on this transition. If the worker is restarted mid-cycle or the same row is polled twice, no recipient receives a duplicate email — idempotency holds.

**Why this priority**: Resubmission is the symmetric back-half of US2 and closes the send-back loop. Without it, reviewers can stall on stale lists. P1 because it pairs with US2 to form the complete review cycle.

**Independent Test**: Run an E2E that submits → sends back → resubmits, and asserts reviewers receive exactly one `RESUBMITTED_BY_APPLICANT` email per resubmission. Then force a second worker pass over the same outbox row and assert no second `NotificationDelivery` row is inserted (idempotency).

**Acceptance Scenarios**:

1. **Given** a sent-back application, **When** the applicant presses Resubmit, **Then** reviewers of the currently-assigned stage group each receive one email and the applicant receives none.
2. **Given** a single resubmission outbox row in `Pending` status, **When** the worker is forced to process it twice in succession, **Then** the second pass is a no-op: no second SMTP send, no second `NotificationDelivery` row.
3. **Given** two sequential resubmissions without an intermediate send-back, **When** the worker dispatches both, **Then** each resubmission produces its own distinct set of emails — the second is not de-duped against the first because the `VersionHistoryId` differs.

---

### User Story 4 - Final approval reaches everyone who matters (Priority: P1)

A reviewer (or approver) records the final approval decision. Within seconds, the applicant receives a `Tu solicitud fue aprobada — {Folio}` email with a deep link to the next-steps surface. Every admin who has explicitly acted on this application also receives the same email so participating admins close the loop on a workflow they invested in. Reviewers as a group do not receive a final-approval notification (they were the ones who approved it).

**Why this priority**: The terminal positive transition is the second-most-anticipated moment for the applicant (after the initial confirmation). P1.

**Independent Test**: Run an E2E that walks an application through to final approval and asserts the applicant receives one `APPLICATION_APPROVED` email with the next-steps deep link, AND every participating admin receives the same email.

**Acceptance Scenarios**:

1. **Given** an application at the final stage with an approval decision recorded, **When** the worker dispatches, **Then** the applicant receives the `Tu solicitud fue aprobada — {Folio}` email and every participating admin receives the same email.
2. **Given** the same final-approval transition, **When** the `NotificationDelivery` rows are inspected, **Then** zero rows exist for the `reviewer` bucket — only `applicant` and (where present) `admin` rows.

---

### User Story 5 - Final rejection reaches everyone who matters (Priority: P1)

A reviewer (or approver) records the final rejection decision. Within seconds, the applicant receives a `Decisión sobre tu solicitud — {Folio}` email with a deep link to the decision-details view. Every participating admin also receives the same email. Reviewers as a group do not receive a final-rejection notification.

**Why this priority**: The terminal negative transition is symmetric with US4. The applicant deserves to be notified with the same speed as approval. P1.

**Independent Test**: Mirror of US4 with `APPLICATION_REJECTED` event and the rejection-decision deep link.

**Acceptance Scenarios**:

1. **Given** an application at the final stage with a rejection decision recorded, **When** the worker dispatches, **Then** the applicant receives the `Decisión sobre tu solicitud — {Folio}` email and every participating admin receives the same email.
2. **Given** the rejection body, **When** the body content is inspected, **Then** no reviewer-internal commentary is embedded verbatim; the body links to the decision detail page where access control is enforced.

---

### User Story 6 - Provider outage does not lose notifications (Priority: P2)

The Mailgun provider (in non-Local) experiences a 30-second outage simulated via sidecar SIGSTOP. During the outage, three workflow events fire and three outbox rows are written. The worker attempts each, fails transiently, increments `AttemptCount`, and schedules backoff. After the sidecar resumes, the worker's next poll picks up the deferred rows and sends them. Every intended recipient eventually receives exactly one email — no losses, no duplicates.

**Why this priority**: The whole point of the outbox + worker architecture is to survive provider hiccups. Without this test passing, the architecture is justified only on paper. P2 because under healthy provider conditions US1–US5 cover the value path.

**Independent Test**: Run an E2E that pauses the SMTP-capture sidecar (SIGSTOP), fires three workflow events, resumes the sidecar (SIGCONT), and asserts the outbox rows reach `Status=Done` within 2 minutes and every intended recipient receives exactly one email.

**Acceptance Scenarios**:

1. **Given** the sidecar is paused and three events fire, **When** the worker polls, **Then** outbox rows remain in `Dispatching` with `AttemptCount > 0` and `NextAttemptAt` advanced per the backoff schedule (1s → 5s → 30s).
2. **Given** the sidecar resumes after 30s, **When** the next worker poll fires, **Then** all three rows transition to `Status=Done` and the SMTP sidecar captures exactly the intended emails.
3. **Given** a permanent failure path (Mailgun returns 4xx for one row), **When** the worker processes it, **Then** that row transitions directly to `Status=DeadLetter` with `AttemptCount=1` and `LastError` populated; no retries.

---

### User Story 7 - Non-prod allowlist guard blocks real users fail-closed (Priority: P2)

Developers and QA run the platform in `Development` or `Staging` environments with `Notifications:NonProdAllowlist` either empty or scoped to `@programa-semilla.test`. A workflow event fires that would, in production, reach an applicant at `real-user@gmail.com`. The allowlist filter drops the recipient and records the drop. The applicant receives nothing. The `NotificationDelivery` row records `Status=BlockedByAllowlist` with `RecipientEmail` and `LastError="NotAllowlisted"`.

**Why this priority**: Without this guard, a staging deployment with production-shaped data can spam real users. The guard is the single most important safety net. P2 because under correct configuration it never fires for legitimate test recipients; correctness is provable in test and visible in logs.

**Independent Test**: Run an integration test with `HostEnvironment=Development` and `Notifications:NonProdAllowlist=[]`, fire one workflow event, and assert: (a) zero SMTP captures, (b) one `NotificationDelivery` row exists with `Status=BlockedByAllowlist`.

**Acceptance Scenarios**:

1. **Given** `HostEnvironment != "Production"` and an empty allowlist, **When** any workflow event fires, **Then** every recipient is dropped and recorded as `BlockedByAllowlist`. Zero emails leave the provider.
2. **Given** an allowlist containing `@programa-semilla.test` and one recipient at `qa@programa-semilla.test`, **When** the same event fires, **Then** the qa recipient receives the email and any other recipient is recorded as `BlockedByAllowlist`.
3. **Given** `HostEnvironment="Production"`, **When** the same event fires, **Then** the allowlist filter is bypassed entirely and all recipients receive their email.

---

### User Story 8 - Participating-admin predicate is correct across role changes (Priority: P3)

An admin who has explicitly acted on application A in the past gets demoted to reviewer between events; they should remain a participating admin for application A. Conversely, a reviewer who is promoted to admin but has *never* explicitly acted on application A is *not* a participating admin and should not receive admin-routed notifications for it. The predicate evaluates current-role + historical-action; passive views do not count.

**Why this priority**: This edge case bites in real-world organizations where role changes are routine. Correctness here prevents both leakage (over-notification of newly-promoted admins on history they don't know) and silence (under-notification of demoted admins on workflows they previously owned). P3 because the predicate is the same query path as US1–US5; this story validates the corner cases.

**Independent Test**: Run an integration test seeding (a) admin-then-reviewer Alice who took an action on app A, (b) reviewer-then-admin Bob who never touched app A, (c) untouched admin Carol. Fire an event on app A. Assert Alice receives the participating-admin email; Bob and Carol do not.

**Acceptance Scenarios**:

1. **Given** Alice (currently a reviewer) has an audit/history entry on application A as a former admin, **When** an event fires on A, **Then** Alice receives the email per the participating-admin path *because she explicitly acted on the application*, regardless of her current role.
2. **Given** Bob (currently an admin) has no audit/history entries on application A, **When** an event fires on A, **Then** Bob receives no email — pure-admin role with no participation does not qualify.
3. **Given** Alice qualifies via both the applicant bucket AND the participating-admin bucket on the same event (rare but possible), **When** dedup runs, **Then** Alice receives exactly one email and the chosen template variant is the applicant-bucket variant (bucket priority `applicant > reviewer > admin`).

---

### Edge Cases

- **EC-001 — Two RESUBMITTED events in succession without an intermediate SendBack.** Each emits its own outbox row with a distinct `VersionHistoryId`; reviewers receive two emails; idempotency does not collapse them because the keys differ.
- **EC-002 — Role change between event-fire and worker-pickup.** Resolver runs at pickup time. A demoted admin who *did* take an explicit action stays in the participating-admin bucket. A promoted reviewer who *never* acted is not pulled in.
- **EC-003 — Applicant email changes between event-fire and worker-pickup.** Resolver uses the *current* email; `PayloadJson` does not snapshot recipient email.
- **EC-004 — Reviewer group reassigned mid-flight (spec 016).** Resubmissions go to the currently-assigned stage group at pickup time, not the group at event-fire time.
- **EC-005 — Application hard-deleted before pickup.** Cascade-delete removes the outbox row; no email sent.
- **EC-006 — One user is applicant on app A and participating-admin on app B; concurrent events.** Two distinct outbox rows, two distinct deliveries — the dedup key differs because `ApplicationId` differs.
- **EC-007 — Email-domain typo at registration.** No DNS pre-flight in v1; Mailgun returns 4xx → outbox row → `DeadLetter`. Operator visibility via `NotificationDelivery.LastError`.
- **EC-008 — Future multi-replica worker.** Row-claim via `RowVersion` optimistic update guarantees a single owner; the unique index on `NotificationDelivery` is the final defense against double-send.
- **EC-009 — Null folio at event time.** Subject template falls back to `Solicitud #{ApplicationId}`. Should not happen post spec 001 but never crashes Razor.
- **EC-010 — SMTP sidecar fails to start at AppHost boot.** Effective provider becomes `NoOpEmailSender` with WARN log; dev workflow not blocked.
- **EC-011 — Mailgun region (US vs EU).** Config-driven `Notifications:Mailgun:BaseUrl`; default `https://api.mailgun.net/v3`. Operators set the EU endpoint via config — no code change.
- **EC-012 — Applicant replies to a notification.** Sender is no-reply; v1 carries no `List-Unsubscribe` header and no automated suppression. Mailgun ToS verification is an open question pinned to planning.
- **EC-013 — Bucket-priority collision.** A user who qualifies for an event via two buckets receives exactly one email; bucket priority `applicant > reviewer > admin` chooses the template variant.
- **EC-014 — Long applicant name in subject template.** Subject is truncated at 78 chars (RFC 5322 line length) with ellipsis appended; full name appears in body.
- **EC-015 — Worker process restart mid-dispatch.** Row left in `Dispatching` with `NextAttemptAt` already advanced; next poll picks it up. The idempotency unique index prevents a double-send if the provider had already accepted the prior attempt.

## Requirements *(mandatory)*

### Functional Requirements

#### Outbox + worker
- **FR-001**: The system MUST write a `NotificationOutbox` row in the SAME database transaction as the workflow state change that triggered it. Failed transactions MUST produce zero outbox rows.
- **FR-002**: A `NotificationOutbox` row MUST carry: `Id` (PK), `EventType` (enum), `ApplicationId` (FK), `VersionHistoryId` (FK to the version row produced by the same transaction), `PayloadJson` (denormalized recipient inputs at write-time), `CreatedAt`, `Status` (`Pending | Dispatching | Done | DeadLetter`), `AttemptCount`, `LastError`, `NextAttemptAt`, `RowVersion`.
- **FR-003**: The system MUST run a single hosted `BackgroundService` worker that polls `NotificationOutbox` for rows where `Status=Pending` OR (`Status=Dispatching` AND `NextAttemptAt <= now`). Poll interval MUST be configurable via `Notifications:Worker:PollIntervalSeconds` (default 5).
- **FR-004**: The worker MUST claim each row with an optimistic update guarded by `RowVersion`, transitioning `Pending → Dispatching`. A losing claim on a contended row MUST be a no-op (retry next poll).
- **FR-005**: After successful dispatch the worker MUST set `Status=Done`. After permanent failure the worker MUST set `Status=DeadLetter`. After transient failure the worker MUST increment `AttemptCount`, set `NextAttemptAt = now + backoff(AttemptCount)`, and leave `Status=Dispatching`.

#### Recipient resolution
- **FR-006**: An `INotificationRecipientResolver` service MUST return the recipient list for a given outbox row, expressed as `(UserId, Email, DisplayName, Bucket, TemplateVariantKey)`. Buckets are `applicant`, `reviewer`, `admin`.
- **FR-007**: For `APPLICATION_SUBMITTED`, the resolver MUST return the applicant (applicant-variant) AND every reviewer of the application's current stage group (reviewer-variant) AND every admin whose user-id appears in the application's `VersionHistory` actor column OR in any existing audit entry tied to this `ApplicationId` (reviewer-variant).
- **FR-008**: For `RETURNED_TO_APPLICANT`, the resolver MUST return the applicant (applicant-variant) AND every participating admin (applicant-variant). The reviewer bucket MUST be empty.
- **FR-009**: For `RESUBMITTED_BY_APPLICANT`, the resolver MUST return every reviewer of the current stage group (reviewer-variant) AND every participating admin (reviewer-variant). The applicant bucket MUST be empty.
- **FR-010**: For `APPLICATION_APPROVED`, the resolver MUST return the applicant (applicant-variant) AND every participating admin (applicant-variant). The reviewer bucket MUST be empty.
- **FR-011**: For `APPLICATION_REJECTED`, the resolver MUST behave per FR-010 with a rejection-flavored template variant.
- **FR-012**: After resolution the system MUST de-duplicate recipients by `UserId`. When a user qualifies via multiple buckets, the chosen template variant MUST follow priority `applicant > reviewer > admin`.
- **FR-013**: The participating-admin predicate MUST query existing reads (`Application.VersionHistory` + existing audit). No new audit infrastructure is introduced.

#### Provider abstraction
- **FR-014**: The system MUST expose an `IEmailSender` interface with three implementations: `MailtrapSmtpEmailSender` (Local SMTP), `MailgunHttpEmailSender` (Mailgun HTTP API), and `NoOpEmailSender` (logs + returns success without sending).
- **FR-015**: The active implementation MUST be selected by `Notifications:Provider` config with sensible per-environment defaults: Local → SMTP-capture sidecar; non-Local → Mailgun; absence of config in non-Production → `NoOpEmailSender` with WARN log.
- **FR-016**: In Production, AppHost MUST fail-fast on boot if `Notifications:Provider=Mailgun` and any of `Notifications:Mailgun:ApiKey`, `Notifications:Mailgun:Domain`, `Notifications:Sender:Email`, or `Notifications:BaseUrl` is missing. The exception message MUST name the missing key.

#### Non-prod allowlist guard
- **FR-017**: When `HostEnvironment != "Production"`, the active `IEmailSender` MUST be wrapped by a `RecipientAllowlistFilter` decorator. The filter MUST drop recipients whose full email or email-domain is NOT in `Notifications:NonProdAllowlist` and MUST record each drop as `NotificationDelivery.Status=BlockedByAllowlist` with `LastError="NotAllowlisted"`.
- **FR-018**: An empty `Notifications:NonProdAllowlist` in non-Production MUST yield zero deliveries to real recipients (fail-closed). The SMTP-capture sidecar path bypasses the filter because the sidecar is a sink.
- **FR-019**: In Production, the filter MUST be bypassed entirely.

#### Idempotency + retry
- **FR-020**: `NotificationDelivery` MUST carry a unique index on `(EventType, ApplicationId, VersionHistoryId, RecipientUserId)`. Before sending, the worker MUST check for an existing row with that key in `Status IN (Sent, BlockedByAllowlist, Skipped)`; if present, the worker MUST no-op and not contact the provider.
- **FR-021**: Transient failures (provider timeout, 5xx, 429) MUST be retried on a backoff schedule of `(1s, 5s, 30s)` over three attempts. After three attempts the outbox row MUST transition to `Status=DeadLetter`.
- **FR-022**: Permanent failures (provider 4xx, hard bounces, template render exceptions, orphaned FKs) MUST transition the outbox row to `Status=DeadLetter` immediately with no retry. The reason MUST be recorded in `LastError`.

#### Templates + content
- **FR-023**: Email bodies MUST be rendered by Razor under a single shared layout partial `_EmailLayout.cshtml` that carries the spec-019 sender display, signature block, footer, and CTA button styling. The layout MUST NOT contain any inline `<img>` element (spec 019 NFR-005 compatibility).
- **FR-024**: The system MUST ship eight body variant partials covering the five events: two variants on `APPLICATION_SUBMITTED` (applicant-confirmation, reviewer-call-to-action), one variant per event on the other four. Each variant MUST render an HTML body AND a plain-text fallback.
- **FR-025**: All template strings MUST be es-CR Spanish. No English fallback. No i18n key system. Subject templates MUST match the table in §Event Catalog.
- **FR-026**: Every CTA button MUST link to a deep-link URL composed from `Notifications:BaseUrl` + a role-specific MVC route. Reviewer/admin CTAs MUST point to `/Reviewer/Applications/Details/{id}`. Applicant CTAs MUST point to `/Applications/Details/{id}`. No new MVC routes are introduced. Access control MUST be enforced server-side by the existing authorize attributes on the target controllers.
- **FR-027**: The spec-019 brand-grep gate (T030) MUST stay green on all new templates. Specifically, no template may contain the strings "Capital Semilla", "Forge", or any English-only copy.

#### Audit + delivery
- **FR-028**: For every send attempt the worker MUST write or update a `NotificationDelivery` row carrying: `Id`, `OutboxId`, `RecipientUserId` (nullable for synthetic addresses), `RecipientEmail`, `Provider`, `ProviderMessageId` (nullable), `Status` (`Sent | Failed | DeadLetter | BlockedByAllowlist | Skipped`), `AttemptCount`, `LastError`, `SentAt`.
- **FR-029**: A recipient with a null or empty email MUST be skipped with `Status=Skipped` and `LastError="MissingEmail"`. Other recipients on the same outbox row MUST still be processed.

#### Aspire dev orchestration + E2E coverage
- **FR-030**: `AppHost.cs` MUST add a containerized SMTP-capture sidecar (smtp4dev or MailHog — pinned during planning) as an Aspire resource. The Web project MUST consume the sidecar's resolved SMTP endpoint via existing Aspire service-discovery config wiring.
- **FR-031**: `tests/FundingPlatform.Tests.E2E/Fixtures/AspireFixture.cs` MUST expose a `MailCaptureClient` that the E2E suite can use to drain and assert captured emails from the sidecar's HTTP API.
- **FR-032**: The placeholder test `tests/FundingPlatform.Tests.E2E/Brand/EmailTemplateSenderTests.cs` (which today calls `Assert.Ignore`) MUST be removed and replaced with real `[Test]` cases per event variant that assert sender display, signature block, no-inline-`<img>`, no "Capital Semilla" / "Forge" leakage, subject template, and the applicant deep-link.

### Non-Functional Requirements

- **NFR-001**: No inline `<img>` in any email body (spec 019 NFR-005 compatibility). Brand wordmark is rendered as text. Partner-logo strip is PDF-only.
- **NFR-002**: P95 time-to-send (from `NotificationOutbox.CreatedAt` to `NotificationDelivery.SentAt`) MUST be under 30 seconds under normal load. P99 MUST be under 2 minutes during retry cycles.
- **NFR-003**: Email body content MUST NOT carry PII beyond what the recipient already has access to in-app. Reviewer / admin emails carry applicant name + folio + stage + CTA. Applicant emails carry the applicant's own folio + status. No legal IDs, no supplier-quote amounts, no internal-reviewer commentary verbatim.
- **NFR-004**: A worker exception MUST NOT crash the Web host. The worker MUST log, leave the affected row in `Dispatching` for the next poll, and continue.
- **NFR-005**: `NotificationOutbox` and `NotificationDelivery` MUST be defined as `.sql` in `FundingPlatform.Database` (dacpac). EF Core Code-First mapping MUST be used for data access. NO EF migrations are introduced. (Constitution §IV.)
- **NFR-006**: All templates MUST be es-CR Spanish. (Constitution §VI + spec 012 hard-pinned culture.)
- **NFR-007**: SMTP-capture sidecar MUST start automatically when developers run `dotnet run --project src/FundingPlatform.AppHost`. Sidecar failure MUST fall back to `NoOpEmailSender` with WARN log, not block the dev workflow.
- **NFR-008**: New configuration keys MUST be added to `CLAUDE.md`'s configuration-knobs table: `Notifications:Provider`, `Notifications:BaseUrl`, `Notifications:NonProdAllowlist`, `Notifications:Mailgun:ApiKey` / `Domain` / `BaseUrl`, `Notifications:Mailtrap:Host` / `Port` / `Username` / `Password`, `Notifications:Worker:PollIntervalSeconds`, `Notifications:Worker:MaxAttempts`, `Notifications:Sender:Name` (default `Programa Semilla / Sistema de Banca para el Desarrollo`), `Notifications:Sender:Email`.

### Event Catalog v1

| Event | Trigger | Subject (es-CR) |
|---|---|---|
| `APPLICATION_SUBMITTED` (reviewer/admin) | `Application.Submit()` | `Nueva solicitud para revisar: {ApplicantName}` |
| `APPLICATION_SUBMITTED` (applicant) | same | `Recibimos tu solicitud — {Folio}` |
| `RETURNED_TO_APPLICANT` | `Application.SendBack()` | `Acción requerida: actualiza tu solicitud — {Folio}` |
| `RESUBMITTED_BY_APPLICANT` | `Application.Resubmit()` | `Solicitud reenviada para revisión: {ApplicantName}` |
| `APPLICATION_APPROVED` | final approval recorded | `Tu solicitud fue aprobada — {Folio}` |
| `APPLICATION_REJECTED` | final rejection recorded | `Decisión sobre tu solicitud — {Folio}` |

### Recipient Rules

| Event | Reviewers of current stage group | Applicant | Admins (participating-action predicate only) |
|---|---|---|---|
| `APPLICATION_SUBMITTED` | yes (review CTA) | yes (confirmation CTA — separate variant) | yes if explicit prior action |
| `RETURNED_TO_APPLICANT` | no | yes (update-and-resubmit CTA) | yes if explicit prior action |
| `RESUBMITTED_BY_APPLICANT` | yes (re-review CTA) | no | yes if explicit prior action |
| `APPLICATION_APPROVED` | no | yes (next-steps CTA) | yes if explicit prior action |
| `APPLICATION_REJECTED` | no | yes (decision-details CTA) | yes if explicit prior action |

Bucket priority on collision: `applicant > reviewer > admin`. One email per `(UserId, Event)`.

### Key Entities

- **NotificationOutbox** — Workflow-event records written transactionally with their triggering state change. Each row is a discrete unit of work for the worker. Fields per FR-002. Retention: pinned during planning (recommended 90 days for `Done`, 1 year for `DeadLetter`).
- **NotificationDelivery** — One row per `(outbox row, recipient)` pair recording the outcome of a send attempt. Fields per FR-028. Carries the unique index that enforces idempotency. Retention: same policy as `NotificationOutbox`.
- **NotificationEvent (enum)** — `APPLICATION_SUBMITTED_REVIEWER`, `APPLICATION_SUBMITTED_APPLICANT`, `RETURNED_TO_APPLICANT`, `RESUBMITTED_BY_APPLICANT`, `APPLICATION_APPROVED`, `APPLICATION_REJECTED`. The split on `APPLICATION_SUBMITTED` into two distinct enum values keeps idempotency keys clean (one outbox row per recipient bucket) and simplifies the recipient resolver.
- **NotificationRecipient** — Resolver output value object. `(UserId, Email, DisplayName, Bucket, TemplateVariantKey)`. Not persisted.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: All six event variants in the §Event Catalog fire on their workflow trigger across the full Aspire stack; verified by at least one E2E test per variant against the SMTP-capture sidecar.
- **SC-002**: Recipient predicate matches the §Recipient Rules table exactly. Verified by an integration test that seeds one applicant, two reviewers in the assigned group, one participating admin, and one non-participating admin, then asserts the predicate yields the expected per-bucket counts on every event.
- **SC-003**: Idempotency holds. Forcing the worker to process the same outbox row twice produces no second `NotificationDelivery` row and no second provider call. Verified by integration test.
- **SC-004**: The non-prod allowlist filter blocks 100% of non-allowlisted recipients when `HostEnvironment != "Production"`. Verified by integration test that runs with `HostEnvironment=Development` and an empty allowlist; zero deliveries leave the provider, every intended recipient is recorded as `BlockedByAllowlist`.
- **SC-005**: `EmailTemplateSenderTests.Assert.Ignore` is removed; the replacement test passes against the SMTP-capture sidecar.
- **SC-006**: The spec-019 brand-grep gate (T030) stays green on all new email templates (zero hits for "Capital Semilla" or "Forge"; zero English-only strings).
- **SC-007**: Provider-outage resilience holds. Simulated 30-second sidecar outage produces zero lost deliveries and zero duplicate deliveries after recovery. Verified by E2E or manual-runnable integration test.
- **SC-008**: Zero new EF migrations are introduced; schema lives entirely in the dacpac. Verified by constitution check during planning + a CI grep gate over `**/Migrations/**`.
- **SC-009**: P95 time-to-send is below 30 seconds across a full E2E run. Verified by aggregating `NotificationDelivery.SentAt - NotificationOutbox.CreatedAt` across the suite.
- **SC-010**: Applicants who submit an application report receiving their confirmation email within one minute, in a usability test or first-party validation pass (qualitative criterion; verifiable by user observation, not by automated test).

## Assumptions

- Workflow state-transition methods (`Submit`, `SendBack`, `Resubmit`, `Approve`, `Reject`) already exist on the `Application` aggregate or its workflow services and persist their changes via a unit-of-work EF transaction that the outbox can hook into. (Verified at planning time.)
- Each state transition produces a `VersionHistory` row inside the same transaction, providing a stable `VersionHistoryId` to anchor the outbox row's idempotency key. (Verified at planning time against spec 002 + spec 004.)
- The applicant's user record carries an email field; reviewer and admin user records likewise. ASP.NET Identity is the authoritative source. (Constitution §III standard.)
- Reviewer-group membership for the current stage of an application is queryable via the spec-016 read path (read-only consumer of `Group` + `UserGroupMembership` + the assigned-group reference on the workflow stage). (Verified at planning time.)
- The `Application` entity exposes a folio or applicant-name string suitable for subject-line interpolation; the resolver reads it once at outbox write time and stores it in `PayloadJson`. (Confirmed at planning against spec 001 / data-model.)
- Mailgun's transactional-email ToS allows automated workflow mail to verified users (the platform's applicants and reviewers) without a List-Unsubscribe header. If it does not, an open question forces a static `mailto:soporte@…` footer line. (Open question OQ-001.)
- `MailKit` v3 (MIT) is acceptable; v4 commercial license posture is verified at planning. Mailgun path uses raw `HttpClient` and introduces no Mailgun-specific NuGet package. (Open question OQ-005.)
- Worker is single-instance for v1 (single Web replica). FR-004 + FR-020 are correct under future multi-replica; throughput tuning is deferred. (Open question OQ-009.)
- The SMTP-capture sidecar (smtp4dev / MailHog) container image is reachable from the dev container registry. (Verified at planning time.)
- Real Mailtrap (cloud) is an opt-in dev override; the default Local provider is the sidecar. (NFR-007 / OQ-003.)

## Dependencies

- **Spec 002 (review-approval-workflow)** — Read-only consumer of `Submit`, `SendBack`, `Approve`, `Reject` transition points. No spec-002 schema change.
- **Spec 004 (applicant-response-appeal)** — Read-only consumer of `Resubmit`. Appeal-specific events are out of scope in v1.
- **Spec 016 (user-groups)** — Read-only consumer of reviewer-group membership for stage-assigned recipient resolution.
- **Spec 019 (programa-semilla-brand)** — Sender display, signature block, brand-grep gate (T030), placeholder test replacement (FR-032). This spec is the deferred counterpart spec 019 explicitly handed off to.
- **Constitution §III** — E2E mandate; SMTP-capture sidecar extends `AspireFixture` (FR-031). Net-new fixture surface.
- **Constitution §IV** — Dacpac `.sql` definitions; no EF migrations. (NFR-005, SC-008.)
- **CLAUDE.md (managed-NuGet rule)** — `MailKit` is a new managed dependency required by `MailtrapSmtpEmailSender`. Approval is embedded in this spec via FR-014. Mailgun path uses raw `HttpClient` (no new dep).
- **`tests/FundingPlatform.Tests.E2E/Fixtures/AspireFixture.cs`** — Touched to host the SMTP-capture sidecar and expose `MailCaptureClient` (FR-031).

## Out of Scope

- In-app notifications, SignalR push, bell icon, toast feed, real-time inbox. (Multi-spec open thread from #08 / #11; remains open after spec 021.)
- Stage-granular events beyond the five (`STAGE_APPROVED`, `MOVED_TO_NEXT_STAGE`, `REVIEWER_ASSIGNED`, `REVIEWER_UNASSIGNED`, `COMMENT_ADDED`).
- Signing-stage events (`AGREEMENT_GENERATED`, `SIGNED_PDF_UPLOADED`).
- User-facing notification-preferences UI / opt-out flow.
- Digests / batching / ML-rank "important" filtering.
- Multi-language template variants. es-CR Spanish only.
- Mailgun bounce-webhook ingestion / suppression-list sync.
- SMS / push / Slack / Teams channels.
- Reply-to-notification ingestion (no-reply sender).
- Public-marketing email (transactional only).

## Open Questions

- **OQ-001 — Mailgun ToS unsubscribe footer.** Does Mailgun require a `List-Unsubscribe` header or visible unsubscribe link for transactional mail to verified users? If yes, the v1 footer carries a static `mailto:soporte@…` line; no automated suppression list. Pin during planning with the Mailgun account owner.
- **OQ-002 — SMTP-capture sidecar choice: smtp4dev vs MailHog.** Both run as containers and expose an HTTP API. `smtp4dev` is .NET-native and lighter; `MailHog` is broadly used and Go-based. Pin during planning.
- **OQ-003 — Real Mailtrap as a dev override.** Sidecar is the default per NFR-007; the override path is documented but the default stays the sidecar. Confirm during planning.
- **OQ-004 — Sender email per environment.** Recommended `no-reply@programa-semilla.cr` for Production; non-Production uses an environment-specific address. Pin with ops.
- **OQ-005 — `MailKit` license posture.** Confirm at planning whether v3 (MIT) or v4 (commercial) is the right pin. If v4 commercial is rejected, fall back to v3.
- **OQ-006 — Idempotency-key composition for SUBMITTED's two-bucket fan-out.** Recommended: split `APPLICATION_SUBMITTED` into two distinct enum values (`_REVIEWER` and `_APPLICANT`) so each outbox row carries one template variant and the unique-index dedup is clean. Confirm during planning.
- **OQ-007 — Folio source-of-truth.** Confirm `Application.Folio` (or equivalent) exists and is populated by the Submit transition; otherwise EC-009's fallback applies.
- **OQ-008 — `NotificationOutbox` retention.** Recommended 90 days for `Done`, 1 year for `DeadLetter`. Pin during planning.
- **OQ-009 — Worker scaling story for future multi-replica.** Correctness is covered by FR-004 + FR-020 + EC-008. Throughput tuning is deferred.
- **OQ-010 — Brand-grep gate render-time vs source-time.** Source-`.cshtml` layer is the recommended scope. Pin during planning.
