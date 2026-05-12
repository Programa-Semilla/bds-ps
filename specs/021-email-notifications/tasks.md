---
description: "Implementation task list for spec 021 Email Notifications System"
---

# Tasks: Email Notifications System

**Input**: Design documents from `/specs/021-email-notifications/`
**Prerequisites**: spec.md (✅), plan.md (✅), research.md (✅), data-model.md (✅), contracts/ (✅), quickstart.md (✅)

**Tests**: Tests are MANDATORY per Constitution §III (E2E) plus FR-031 / FR-032 / SC-001..010. Test tasks are bundled with their owning user story.

**Organization**: Tasks are grouped by user story. P1 stories (US1–US5) are required for MVP. P2 stories (US6, US7) and the P3 story (US8) layer on after the v1 happy path is green.

## Format: `[ID] [P?] [Story?] Description with file path`

- **[P]** — Can run in parallel (different files, no dependencies on incomplete tasks)
- **[USn]** — User-story owner (US1..US8 from spec.md). Setup, foundational, and polish phases have no story label.

## Path Conventions

Single-solution Clean Architecture monolith. Paths are relative to repo root.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Land the project-level scaffolding needed by every user story — managed-NuGet approval, Aspire sidecar, config knobs, dacpac tables, EF mapping, DI bootstrap.

- [x] T001 Add `MailKit` v3 (MIT) managed-NuGet reference to `src/FundingPlatform.Infrastructure/FundingPlatform.Infrastructure.csproj`. Pin to a v3.x version. Justify in `CLAUDE.md` Active Technologies (already done in spec 021 plan commit).
- [x] T002 [P] Create `src/FundingPlatform.Database/Tables/dbo.NotificationOutbox.sql` per [data-model.md](./data-model.md) — full table DDL + `IX_NotificationOutbox_Status_NextAttemptAt` + `IX_NotificationOutbox_ApplicationId`.
- [x] T003 [P] Create `src/FundingPlatform.Database/Tables/dbo.NotificationDelivery.sql` per [data-model.md](./data-model.md) — full table DDL + `UX_NotificationDelivery_DedupKey` (filtered unique index) + `IX_NotificationDelivery_OutboxId` + `IX_NotificationDelivery_RecipientEmail`.
- [x] T004 Verify dacpac builds cleanly: `dotnet build src/FundingPlatform.Database/FundingPlatform.Database.sqlproj`. Fix any cross-FK reference order issues against `dbo.Applications` and `dbo.VersionHistory`.
- [x] T005 Add smtp4dev container resource to `src/FundingPlatform.AppHost/AppHost.cs` per `research.md` R-007. Wire `webApp.WithReference(smtp4dev.GetEndpoint("smtp")).WithReference(smtp4dev.GetEndpoint("http")).WaitFor(smtp4dev)`.
- [x] T006 Add new configuration keys to `src/FundingPlatform.Web/appsettings.json` + `appsettings.Development.json` (defaults only): `Notifications:Provider`, `Notifications:BaseUrl`, `Notifications:NonProdAllowlist`, `Notifications:Mailgun:{ApiKey,Domain,BaseUrl}`, `Notifications:Mailtrap:{Host,Port,Username,Password}`, `Notifications:Worker:{PollIntervalSeconds,MaxAttempts,BatchSize}`, `Notifications:Sender:{Name,Email}`. Defaults match the table in plan.md.
- [x] T007 Update `CLAUDE.md` configuration-knobs table (already inserted in plan checkpoint — verify after rebase that all keys from T006 appear in the table). Re-confirm Programa Semilla sender display in `Notifications:Sender:Name`.
- [x] T008 [P] Create the empty namespaces and target-file folders required by Phase 2 entities + interfaces: `src/FundingPlatform.Domain/Notifications/`, `src/FundingPlatform.Application/Notifications/`, `src/FundingPlatform.Application/Notifications/Templates/`, `src/FundingPlatform.Infrastructure/Notifications/{Persistence,Providers,Resolvers,Templating,Workers,DependencyInjection}/`, `src/FundingPlatform.Web/Views/Emails/`, `tests/FundingPlatform.Tests.Unit/Notifications/`, `tests/FundingPlatform.Tests.Integration/Notifications/`, `tests/FundingPlatform.Tests.E2E/Notifications/`.

**Checkpoint**: Solution builds. Dacpac deploys cleanly via `dotnet run --project src/FundingPlatform.AppHost`. The smtp4dev container appears in the Aspire dashboard as Healthy.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Land the cross-cutting types every user story depends on. **No user-story work begins until this phase is complete.**

### Domain layer

- [x] T009 Create `src/FundingPlatform.Domain/Notifications/NotificationEvent.cs` — enum with 6 values (`ApplicationSubmittedReviewer`, `ApplicationSubmittedApplicant`, `ReturnedToApplicant`, `ResubmittedByApplicant`, `ApplicationApproved`, `ApplicationRejected`). Add an `EnumStringConverter`-friendly extension method `ToStorageString()` mapping to upper-snake-case (`APPLICATION_SUBMITTED_REVIEWER`, …).

### Application layer (interfaces + value objects)

- [x] T010 [P] Create `src/FundingPlatform.Application/Notifications/IEmailSender.cs` per [contracts/IEmailSender.md](./contracts/IEmailSender.md) — interface + `EmailMessage`, `EmailSendResult`, `EmailSendOutcome` records/enums.
- [x] T011 [P] Create `src/FundingPlatform.Application/Notifications/RecipientBucket.cs` — enum (`Applicant=1, Reviewer=2, Admin=3`).
- [x] T012 [P] Create `src/FundingPlatform.Application/Notifications/NotificationRecipient.cs` — `record(UserId?, Email, DisplayName, Bucket, TemplateVariantKey)`.
- [x] T013 [P] Create `src/FundingPlatform.Application/Notifications/NotificationPayload.cs` — `record(ApplicationId, ApplicantUserId, ApplicantDisplayName, StageGroupIds, OutcomeCode?)`. Static `Serialize`/`Deserialize` helpers around `System.Text.Json`.
- [x] T014 [P] Create `src/FundingPlatform.Application/Notifications/INotificationRecipientResolver.cs` per [contracts/INotificationRecipientResolver.md](./contracts/INotificationRecipientResolver.md).
- [x] T015 [P] Create `src/FundingPlatform.Application/Notifications/INotificationOutboxWriter.cs` — interface `Task EnqueueAsync(NotificationEvent eventType, int applicationId, int versionHistoryId, NotificationPayload payload, CancellationToken ct)`. The Application Service calls this between `AddVersionHistory(...)` and `SaveChangesAsync()`.
- [x] T016 [P] Create `src/FundingPlatform.Application/Notifications/Templates/NotificationTemplateBindings.cs` — static map `NotificationEvent → (SubjectTemplate, HtmlViewName, TextViewName, TemplateVariantKey)`. Subject templates use `Solicitud #{Application.Id}` not `{Folio}`.

### Infrastructure layer (entities, EF, renderer, DI bootstrap)

- [x] T017 Create `src/FundingPlatform.Infrastructure/Notifications/Persistence/NotificationOutbox.cs` — EF-mapped class with private setters, factory `Create(...)`, behavior methods `ClaimForDispatch()`, `MarkDone()`, `MarkTransientFailure(error, nextAttemptAt)`, `MarkDeadLetter(error)`. RowVersion property.
- [x] T018 Create `src/FundingPlatform.Infrastructure/Notifications/Persistence/NotificationDelivery.cs` — EF-mapped class, factory methods `RecordSend(...)`, `RecordTransientFailure(...)`, `RecordPermanentFailure(...)`, `RecordSkipped(...)`, `RecordBlockedByAllowlist(...)`.
- [x] T019 Create `src/FundingPlatform.Infrastructure/Notifications/Persistence/NotificationOutboxConfiguration.cs` — `IEntityTypeConfiguration<NotificationOutbox>`. Maps to `dbo.NotificationOutbox`, sets `RowVersion` as `.IsRowVersion().IsConcurrencyToken()`, registers `EventType` as a string converter against the enum.
- [x] T020 Create `src/FundingPlatform.Infrastructure/Notifications/Persistence/NotificationDeliveryConfiguration.cs` — `IEntityTypeConfiguration<NotificationDelivery>`. Maps to `dbo.NotificationDelivery`, declares the filtered unique index.
- [x] T021 Edit `src/FundingPlatform.Infrastructure/Persistence/FundingPlatformDbContext.cs` — register `DbSet<NotificationOutbox>` and `DbSet<NotificationDelivery>`. Call `modelBuilder.ApplyConfiguration(new NotificationOutboxConfiguration())` and the delivery configuration in `OnModelCreating`. (Note: actual file is `AppDbContext.cs`; `ApplyConfigurationsFromAssembly` picks up the new IEntityTypeConfigurations automatically.)
- [x] T022 Create `src/FundingPlatform.Infrastructure/Notifications/Persistence/NotificationOutboxWriter.cs` — implements `INotificationOutboxWriter`. Calls `_context.Set<NotificationOutbox>().Add(row)`. Does NOT call `SaveChangesAsync`; relies on the Application Service's pending unit-of-work commit.
- [x] T023 [P] Create `RazorEmailRenderer` — uses `IRazorViewEngine` + `ITempDataProvider` to render `Views/Emails/{viewName}.cshtml` off-thread. Renders BOTH the HTML body and the plain-text fallback (separate views, suffix `.text.cshtml`). Throws `EmailRenderException` on render failure; the worker maps it to `PermanentFailure`. (Lives in `src/FundingPlatform.Web/Services/` because it depends on ASP.NET Core MVC types — same pattern as `RazorFundingAgreementHtmlRenderer`. The Application-layer `IEmailTemplateRenderer` abstraction is what Infrastructure consumes.)
- [x] T024 [P] Create `src/FundingPlatform.Web/Views/Emails/_EmailLayout.cshtml` — shared layout per FR-023: text-only spec-019 wordmark, signature block, static `mailto:soporte@programa-semilla.cr` footer line. No inline `<img>`. CTA button styling (inline CSS for email-client compat).
- [x] T025 [P] Create `src/FundingPlatform.Web/Views/Emails/_SupportFooter.cshtml` — partial reused by every body variant; renders the support mailto + Programa Semilla footer copy.
- [x] T026 Create `NotificationsServiceCollectionExtensions.AddNotifications` — registers: `INotificationOutboxWriter` → `NotificationOutboxWriter`, `INotificationRecipientResolver` → `NotificationRecipientResolver`, `IEmailSender` → provider per the selection matrix in `contracts/IEmailSender.md`, `IEmailTemplateRenderer` → `RazorEmailRenderer`, the `EmailDispatchWorker` `IHostedService`. In Production, FAILS FAST if `Notifications:Provider=Mailgun` is missing required keys (FR-016). (Lives in `src/FundingPlatform.Web/Services/` so it can wire the Razor renderer.)
- [x] T027 Edit `src/FundingPlatform.Web/Program.cs` to call `builder.Services.AddNotifications(builder.Configuration, builder.Environment)` after the existing service registrations.

### Test harness

- [x] T028 Create `tests/FundingPlatform.Tests.E2E/Fixtures/MailCaptureClient.cs` per [contracts/MailCaptureClient.md](./contracts/MailCaptureClient.md) — `ListAsync`, `WaitForAsync(minCount, timeout, filter)`, `DrainAsync`, internal `CapturedMessage` record.
- [x] T029 Edit `tests/FundingPlatform.Tests.E2E/Fixtures/AspireFixture.cs` — create the `HttpClient` against the smtp4dev http endpoint; instantiate `MailCaptureClient` and expose as `public MailCaptureClient? MailCapture { get; }` (nullable; null in NFR-007 degraded mode). Dispose in `DisposeAsync`.

**Checkpoint**: Solution compiles. `dotnet test tests/FundingPlatform.Tests.Unit` passes (no notification tests yet). Aspire dashboard shows all four resources Healthy. `MailCaptureClient` can list zero messages successfully.

---

## Phase 3: User Story 1 — Applicant submits and the workflow speaks back (Priority: P1) 🎯 MVP

**Goal**: First-time `Application.Submit()` produces exactly one applicant-variant email AND one reviewer-variant email per reviewer in the assigned intake group. Participating admins (current admin role + at least one prior `VersionHistory` row on this app) get the reviewer-flavored email. No duplicates. Sender display + signature + es-CR locale + no inline `<img>`.

**Independent Test**: `ApplicationSubmittedNotificationsTests.SubmitFiresApplicantAndReviewerVariants` runs an E2E that signs in as applicant, submits, asserts exactly `1 + #reviewers` captured messages with the expected subjects and CTAs.

### Resolver + outbox writer

- [ ] T030 [P] [US1] Create `src/FundingPlatform.Infrastructure/Notifications/Resolvers/NotificationRecipientResolver.cs` — implements `INotificationRecipientResolver`. Bucket dedup per FR-012. Stage-group reviewer query reads via `FundingPlatformDbContext` (UserGroupMembership + current stage's assigned groups). Returns recipients in deterministic order (applicant first, then reviewers by user id ascending, then participating admins by user id ascending).
- [ ] T031 [US1] Create `src/FundingPlatform.Infrastructure/Notifications/Resolvers/ParticipatingAdminPredicate.cs` — encapsulates the v1 predicate from `research.md` R-006 (current-Admin-role-only). Returns `IQueryable<string> userIds`. Documented v1 limitation re EC-002 / OQ-011.

### Workflow hook (Application Service)

- [ ] T032 [US1] Edit `src/FundingPlatform.Application/Services/ApplicationService.cs` (the existing `SubmitApplicationAsync` / equivalent method) to: (a) determine whether this submission is a first-time submit vs. resubmit by querying `VersionHistory` for any prior `Action="SendBack"` row on the same `ApplicationId`; (b) for FIRST-TIME submit only — enqueue TWO outbox rows in one transaction (`APPLICATION_SUBMITTED_REVIEWER` and `APPLICATION_SUBMITTED_APPLICANT`) via the injected `INotificationOutboxWriter`. Place the call BETWEEN `application.AddVersionHistory(...)` and `_applicationRepository.SaveChangesAsync()`. Resubmit detection is handled in US3.

### Templates

- [ ] T033 [P] [US1] Create `src/FundingPlatform.Web/Views/Emails/ApplicationSubmittedApplicant.cshtml` (HTML) — subject template `Recibimos tu solicitud — Solicitud #{Application.Id}`; body confirming receipt, CTA to `/Application/Details/{id}` composed from `Notifications:BaseUrl`.
- [ ] T034 [P] [US1] Create `src/FundingPlatform.Web/Views/Emails/ApplicationSubmittedApplicant.text.cshtml` (plain text fallback).
- [ ] T035 [P] [US1] Create `src/FundingPlatform.Web/Views/Emails/ApplicationSubmittedReviewer.cshtml` (HTML) — subject `Nueva solicitud para revisar: {ApplicantName}`; body announcing new work, CTA to `/Review/{id}`.
- [ ] T036 [P] [US1] Create `src/FundingPlatform.Web/Views/Emails/ApplicationSubmittedReviewer.text.cshtml`.

### Worker (minimum viable dispatcher — single attempt, no retry yet)

- [ ] T037 [US1] Create `src/FundingPlatform.Infrastructure/Notifications/Workers/EmailDispatchWorker.cs` — hosted `BackgroundService`. Poll loop: `await Task.Delay(pollInterval, ct); ProcessBatchAsync(ct);`. `ProcessBatchAsync` selects `Pending` rows ordered by `CreatedAt`, claims via `RowVersion` optimistic update (`Pending → Dispatching`), resolves recipients, checks `NotificationDelivery` for an existing dedup row before sending, calls `IEmailSender.SendAsync(...)`, writes `NotificationDelivery` + transitions outbox to `Done`. For US1 the worker handles `Sent` outcome only; retry + dead-letter come in US6.
- [ ] T038 [US1] Create `src/FundingPlatform.Infrastructure/Notifications/Providers/NoOpEmailSender.cs` — logs WARN, returns `EmailSendOutcome.Sent` with null `ProviderMessageId`. Used as default when no provider config is set (FR-015).
- [ ] T039 [US1] Create `src/FundingPlatform.Infrastructure/Notifications/Providers/MailtrapSmtpEmailSender.cs` — MailKit v3 path. Builds `MimeMessage` with `multipart/alternative`, sends via `SmtpClient` to the configured (or Aspire-discovered) host/port. Maps SMTP outcomes per the table in `contracts/IEmailSender.md`. For US1, the smtp4dev sidecar accepts everything → always `Sent`.

### Unit tests

- [ ] T040 [P] [US1] Create `tests/FundingPlatform.Tests.Unit/Notifications/NotificationTemplateBindingsTests.cs` — assert every `NotificationEvent` enum value has a binding row (subject template, html view name, text view name, variant key).
- [ ] T041 [P] [US1] Create `tests/FundingPlatform.Tests.Unit/Notifications/RazorEmailRendererTests.cs` — render EVERY template variant (all 6 events × HTML + text) against fixture models; assert no inline `<img>`, sender display string in body, no `Capital Semilla` / `Forge`, es-CR copy, **no PII leakage beyond what the spec allows in each variant per NFR-003** (e.g., reviewer/admin templates carry applicant name + folio-id + stage + CTA only; applicant templates carry the applicant's own folio + status only; no legal IDs, no supplier-quote amounts, no reviewer-internal commentary verbatim).

### Integration test

- [ ] T042 [US1] Create `tests/FundingPlatform.Tests.Integration/Notifications/OutboxTransactionalEnqueueTests.cs` — `Submit_writes_two_outbox_rows_in_one_tx`. Also: `Submit_fails_writes_zero_outbox_rows` (validation failure path — FR-001).

### E2E tests (mandatory per Constitution §III)

- [ ] T043 [US1] Create `tests/FundingPlatform.Tests.E2E/Notifications/ApplicationSubmittedNotificationsTests.cs` — full flow: applicant submits → wait for `1 + #reviewers + #participatingAdmins` captures → assert subjects + CTA hrefs + sender display + no inline `<img>` + no brand-grep hits + zero `Capital Semilla` / `Forge` / English-only strings.

**Checkpoint**: US1 demoable end-to-end against the smtp4dev sidecar. Solution builds + unit + integration + US1 E2E test green. MVP scope reached.

---

## Phase 4: User Story 2 — Reviewer sends back, applicant gets called to action (Priority: P1)

**Goal**: `Application.SendBack()` produces one applicant-variant email + participating-admin copies. Zero reviewer emails.

**Independent Test**: `ReturnedToApplicantNotificationsTests` — submit → send back → assert exactly one applicant-variant email + #participatingAdmins emails, zero reviewer-variant.

- [ ] T044 [US2] Edit `src/FundingPlatform.Application/Services/ReviewService.cs` (or whatever service owns `Application.SendBack` invocation) — enqueue ONE outbox row with `EventType=RETURNED_TO_APPLICANT` between `AddVersionHistory` and `SaveChangesAsync`.
- [ ] T045 [P] [US2] Create `src/FundingPlatform.Web/Views/Emails/ReturnedToApplicant.cshtml` — subject `Acción requerida: actualiza tu solicitud — Solicitud #{Application.Id}`; CTA to `/Application/Details/{id}`.
- [ ] T046 [P] [US2] Create `src/FundingPlatform.Web/Views/Emails/ReturnedToApplicant.text.cshtml`.
- [ ] T047 [US2] Extend `NotificationRecipientResolver.cs` (T030) to support `RETURNED_TO_APPLICANT`: applicant bucket + participating-admin bucket; reviewer bucket empty (FR-008).
- [ ] T048 [US2] Create `tests/FundingPlatform.Tests.E2E/Notifications/ReturnedToApplicantNotificationsTests.cs` — full Submit-then-SendBack flow; assert applicant email present, reviewer-variant absent. Verify current-email-not-snapshot path (EC-003) by changing the applicant's email between Submit and SendBack and asserting delivery to the new address.

**Checkpoint**: US2 E2E green.

---

## Phase 5: User Story 3 — Applicant resubmits, reviewers re-engaged, no duplicates (Priority: P1)

**Goal**: `Application.Submit()` invoked after a prior `SendBack()` fires `RESUBMITTED_BY_APPLICANT` instead of `APPLICATION_SUBMITTED_*`. Reviewers get one email; applicant gets none. Idempotency holds under worker double-process.

**Independent Test**: `ResubmittedNotificationsTests` — submit → send back → resubmit → assert exactly #reviewers emails; second worker pass over the same outbox row produces zero additional deliveries.

- [ ] T049 [US3] Extend `src/FundingPlatform.Application/Services/ApplicationService.cs` (T032) — when the prior-SendBack query returns a row, enqueue ONE outbox row with `EventType=RESUBMITTED_BY_APPLICANT` instead of the two-row APPLICATION_SUBMITTED_* fan-out.
- [ ] T050 [P] [US3] Create `src/FundingPlatform.Web/Views/Emails/ResubmittedByApplicant.cshtml` — subject `Solicitud reenviada para revisión: {ApplicantName}`; CTA to `/Review/{id}`.
- [ ] T051 [P] [US3] Create `src/FundingPlatform.Web/Views/Emails/ResubmittedByApplicant.text.cshtml`.
- [ ] T052 [US3] Extend `NotificationRecipientResolver.cs` to support `RESUBMITTED_BY_APPLICANT`: reviewer bucket + participating-admin bucket; applicant bucket empty (FR-009).
- [ ] T053 [US3] Create `tests/FundingPlatform.Tests.Integration/Notifications/IdempotencyDoubleProcessTests.cs` — set up one outbox row, force the worker's `ProcessBatchAsync` twice; assert second pass is a no-op (no new `NotificationDelivery` rows). Covers SC-003.
- [ ] T054 [US3] Create `tests/FundingPlatform.Tests.Integration/Notifications/SequentialResubmitTests.cs` — two resubmissions without an intermediate SendBack produce two distinct outbox rows with different `VersionHistoryId`, each fanning out independently (EC-001).
- [ ] T055 [US3] Create `tests/FundingPlatform.Tests.E2E/Notifications/ResubmittedNotificationsTests.cs` — full Submit→SendBack→Resubmit flow; assert reviewer captures + zero applicant captures.

**Checkpoint**: US3 E2E green. Idempotency proven by integration test.

---

## Phase 6: User Story 4 — Final approval reaches everyone who matters (Priority: P1)

**Goal**: `Application.Finalize(force=false)` with a derived `Approved` outcome fires `APPLICATION_APPROVED` → applicant + participating admins. Zero reviewer emails.

**Independent Test**: `ApprovedAndRejectedNotificationsTests.Approve_fires_approval_emails` — walk an application to final approval; assert applicant + admin captures with the approval subject.

- [ ] T056 [US4] Edit `src/FundingPlatform.Application/Services/ReviewService.cs` (or whatever service invokes `Application.Finalize`) — after `Finalize`, derive the application's terminal outcome (all items approved → `OutcomeCode="Approved"`; otherwise → `"Rejected"`). Enqueue ONE outbox row with `EventType=APPLICATION_APPROVED` when outcome is Approved, between `AddVersionHistory` and `SaveChangesAsync`. (Rejected branch in US5.)
- [ ] T057 [P] [US4] Create `src/FundingPlatform.Web/Views/Emails/ApplicationApproved.cshtml` — subject `Tu solicitud fue aprobada — Solicitud #{Application.Id}`; CTA to `/Application/Details/{id}` (next-steps surface).
- [ ] T058 [P] [US4] Create `src/FundingPlatform.Web/Views/Emails/ApplicationApproved.text.cshtml`.
- [ ] T059 [US4] Extend `NotificationRecipientResolver.cs` to support `APPLICATION_APPROVED`: applicant bucket + participating-admin bucket; reviewer bucket empty (FR-010).
- [ ] T060 [US4] Add the approval branch to `tests/FundingPlatform.Tests.E2E/Notifications/ApprovedAndRejectedNotificationsTests.cs`.

**Checkpoint**: US4 E2E green.

---

## Phase 7: User Story 5 — Final rejection reaches everyone who matters (Priority: P1)

**Goal**: `Application.Finalize(...)` with a derived `Rejected` outcome fires `APPLICATION_REJECTED` → applicant + participating admins.

**Independent Test**: `ApprovedAndRejectedNotificationsTests.Reject_fires_rejection_emails` — same as US4 with rejection variant.

- [ ] T061 [US5] Extend `src/FundingPlatform.Application/Services/ReviewService.cs` (T056) — the Rejected branch enqueues an outbox row with `EventType=APPLICATION_REJECTED`.
- [ ] T062 [P] [US5] Create `src/FundingPlatform.Web/Views/Emails/ApplicationRejected.cshtml` — subject `Decisión sobre tu solicitud — Solicitud #{Application.Id}`; CTA to `/Application/Details/{id}`. Body MUST NOT contain reviewer-internal commentary verbatim (NFR-003).
- [ ] T063 [P] [US5] Create `src/FundingPlatform.Web/Views/Emails/ApplicationRejected.text.cshtml`.
- [ ] T064 [US5] Extend `NotificationRecipientResolver.cs` to support `APPLICATION_REJECTED` (FR-011 = FR-010 with rejection variant).
- [ ] T065 [US5] Add the rejection branch to `tests/FundingPlatform.Tests.E2E/Notifications/ApprovedAndRejectedNotificationsTests.cs` — assert subject + body shape; assert no reviewer-comment leakage.

**Checkpoint**: US5 E2E green. Full P1 happy-path complete.

---

## Phase 8: User Story 6 — Provider outage does not lose notifications (Priority: P2)

**Goal**: Transient failures retry with backoff `(1s, 5s, 30s)` across three attempts. Permanent failures (HTTP 4xx, render exceptions) go straight to `DeadLetter`.

**Independent Test**: `ProviderOutageResilienceTests` — SIGSTOP smtp4dev, fire three events, SIGCONT, assert all three reach `Status=Done` within 2 minutes with exactly one captured email each (no duplicates, no losses).

- [ ] T066 [US6] Extend `src/FundingPlatform.Infrastructure/Notifications/Workers/EmailDispatchWorker.cs` (T037) — implement the retry/backoff loop: on `EmailSendOutcome.TransientFailure`, increment `AttemptCount`, set `NextAttemptAt = now + backoffSchedule[AttemptCount - 1]`, leave `Status=Dispatching`. On reaching `MaxAttempts` (default 3), transition to `DeadLetter`. On `PermanentFailure`, transition to `DeadLetter` immediately (FR-021, FR-022).
- [ ] T067 [US6] Create `src/FundingPlatform.Infrastructure/Notifications/Providers/MailgunHttpEmailSender.cs` — raw `HttpClient` POST to `${BaseUrl}/${Domain}/messages` with Basic auth `api:${ApiKey}` and `multipart/form-data` body. Maps response to `EmailSendOutcome` per the table in `contracts/IEmailSender.md`.
- [ ] T068 [P] [US6] Create `tests/FundingPlatform.Tests.Unit/Notifications/EmailDispatchWorkerTests.cs` — backoff math, claim-loss semantics, MaxAttempts → DeadLetter transition, PermanentFailure → DeadLetter immediately.
- [ ] T069 [P] [US6] Create `tests/FundingPlatform.Tests.Unit/Notifications/MailgunHttpEmailSenderTests.cs` — `HttpMessageHandler` mock per error-classification row.
- [ ] T070 [US6] Create `tests/FundingPlatform.Tests.Integration/Notifications/DeadLetterPathTests.cs` — feed a `PermanentFailure` mock → assert one `NotificationDelivery` row with `Status=DeadLetter`, `AttemptCount=1`, outbox row `Status=DeadLetter`.
- [ ] T071 [US6] Create `tests/FundingPlatform.Tests.E2E/Notifications/ProviderOutageResilienceTests.cs` — SIGSTOP/SIGCONT the smtp4dev container (via Docker API or `docker pause`/`docker unpause` shell-out from the fixture); fire three events; assert eventual success with zero duplicates within 2 minutes.

**Checkpoint**: US6 E2E green. SC-007 (provider-outage resilience) met.

---

## Phase 9: User Story 7 — Non-prod allowlist guard blocks real users fail-closed (Priority: P2)

**Goal**: `RecipientAllowlistFilter` drops every non-allowlisted recipient outside Production and records `Status=BlockedByAllowlist`. Empty allowlist → zero emails leave.

**Independent Test**: `AllowlistGuardE2ETests` — set `Notifications:NonProdAllowlist=[]`, fire one workflow event, assert zero captured messages + one `BlockedByAllowlist` row per intended recipient.

- [ ] T072 [US7] Create `src/FundingPlatform.Infrastructure/Notifications/RecipientAllowlistFilter.cs` — `IEmailSender` decorator. Reads `Notifications:NonProdAllowlist` from `IConfiguration`. Returns `EmailSendOutcome.BlockedByAllowlist` and DOES NOT call the wrapped sender when the recipient is not allowlisted (FR-017, FR-018).
- [ ] T073 [US7] Edit `NotificationsServiceCollectionExtensions` (T026) — register `RecipientAllowlistFilter` as the outermost `IEmailSender` decorator when `HostEnvironment != "Production"`. Production resolves the bare sender (FR-019).
- [ ] T074 [US7] Edit `EmailDispatchWorker` (T037/T066) — on `EmailSendOutcome.BlockedByAllowlist`, write a `NotificationDelivery` row with `Status=BlockedByAllowlist` + `LastError="NotAllowlisted"`; outbox row transitions to `Done` (it is not a failure — the worker successfully handled it).
- [ ] T075 [P] [US7] Create `tests/FundingPlatform.Tests.Unit/Notifications/RecipientAllowlistFilterTests.cs` — drop / pass-through / production-bypass cases.
- [ ] T076 [US7] Create `tests/FundingPlatform.Tests.Integration/Notifications/AllowlistFailClosedTests.cs` — `HostEnvironment=Development` + empty allowlist → zero deliveries leave the (mocked) provider; one `BlockedByAllowlist` row per recipient. Covers SC-004.
- [ ] T077 [US7] Create `tests/FundingPlatform.Tests.E2E/Notifications/AllowlistGuardE2ETests.cs` — full Aspire boot with allowlist override; assert `MailCapture.ListAsync()` returns 0 messages even after firing an event.

**Checkpoint**: US7 E2E green. SC-004 met.

---

## Phase 10: User Story 8 — Participating-admin predicate is correct across role changes (Priority: P3)

**Goal**: Validate the v1 participating-admin predicate behavior matrix. **Note**: per `research.md` R-006, EC-002 is only partially supported in v1 — the demoted-admin case is NOT covered. Test acknowledges and documents the known limitation (OQ-011).

**Independent Test**: `ParticipatingAdminPredicateTests` — seed three users (Alice currently-reviewer-but-acted, Bob currently-admin-no-action, Carol currently-admin-no-action); fire an event; assert per-bucket counts.

- [ ] T078 [US8] Create `tests/FundingPlatform.Tests.Integration/Notifications/ParticipatingAdminPredicateTests.cs` — three subcases:
  - `CurrentAdminWithVersionHistory_isIncluded` (PASS in v1)
  - `CurrentReviewerWithVersionHistory_isExcluded` (FAILS by design in v1 — assertion marks the test as `[Test, Explicit("OQ-011 — deferred to a future spec")]` until the predicate is extended). Document the limitation in test summary XML doc.
  - `CurrentAdminWithoutVersionHistory_isExcluded` (PASS in v1).
- [ ] T079 [US8] Create `tests/FundingPlatform.Tests.Integration/Notifications/DedupBucketPriorityTests.cs` — a user qualifying as applicant + admin (rare) receives one row with `Bucket=Applicant` and applicant-variant template. Covers US8 acceptance scenario 3.
- [ ] T080 [US8] Create `tests/FundingPlatform.Tests.Integration/Notifications/MissingEmailSkipTests.cs` — applicant with null email → recipient row → `Status=Skipped` + `LastError="MissingEmail"`. Other recipients on the same outbox row still process (FR-029).

**Checkpoint**: US8 integration tests green. v1 limitation documented and isolated behind `[Explicit]`.

---

## Phase 11: Polish & Cross-Cutting Concerns

**Purpose**: Replace placeholder test, brand-grep gate, retention notes, performance gate, documentation.

- [ ] T081 Replace the body of `tests/FundingPlatform.Tests.E2E/Brand/EmailTemplateSenderTests.cs` — remove the `Assert.Ignore` placeholder. Add one `[Test]` per event variant. Each asserts: sender display, signature block, no inline `<img>`, no `Capital Semilla`/`Forge`, subject template renders correctly. Per FR-032. Preserve namespace and class name.
- [ ] T082 [P] Verify brand-grep gate T030 (from spec 019) stays green on all new templates: `grep -r -E 'Capital Semilla|Forge' src/FundingPlatform.Web/Views/Emails/` returns empty. CI grep gate update if needed.
- [ ] T083 [P] Add a perf assertion to `ApplicationSubmittedNotificationsTests` — read `NotificationOutbox.CreatedAt` and `NotificationDelivery.SentAt` for each row; assert P95 across the test under 30 s. Covers SC-009 / NFR-002.
- [ ] T084 [P] Add `EmailRenderException` permanent-failure path coverage to `EmailDispatchWorkerTests` — render exception → `DeadLetter` with `LastError` populated.
- [ ] T085 Add an `Active Technologies` line and `Recent Changes` entry referencing 021 to `CLAUDE.md` (done in plan checkpoint; verify after rebase + add the smtp4dev row to the configuration-knobs table).
- [ ] T086 Run the **full** E2E suite locally: `dotnet test tests/FundingPlatform.Tests.E2E`. Confirm 100% green. Per memory feedback `delivery_requires_e2e_green`, NOTHING ships until this passes.

---

## Dependencies

```
Phase 1 (Setup)
   └── Phase 2 (Foundational)
          └── Phase 3 (US1) ──────────────────┐
                Phase 4 (US2) ──────────────┐ │
                Phase 5 (US3, needs US1's hook code) ────┐
                Phase 6 (US4) ──────────────┐ │ │
                Phase 7 (US5, needs US4's outcome derivation) ──┐
                Phase 8 (US6, layers retry onto US1's worker) ───────┐
                Phase 9 (US7, layers decorator onto any US1..US5) ────┐ │
                Phase 10 (US8, validates resolver from US1) ──────────┐ │ │
                                                                                  └── Phase 11 (Polish)
```

**Critical path for MVP (P1)**: Phase 1 → Phase 2 → Phase 3 (US1). All other phases consume Phase 2 artifacts.

**Parallelism**:

- US2, US3, US4, US5 templates/views can be authored in parallel with US1 once Phase 2 is done (different files).
- US6 retry logic and US7 allowlist decorator are independent of each other and can be developed in parallel once US1's worker exists.
- US8 tests validate Phase 2's resolver and can run as soon as Phase 2 ships.

## Implementation Strategy

**MVP scope** (minimum shippable): Phase 1 + Phase 2 + Phase 3 (US1). Delivers the closed-loop applicant + reviewer notification on the first submit — the highest-value user-felt promise. Everything else layers on top.

**Recommended order**:

1. Phase 1 → checkpoint commit
2. Phase 2 → checkpoint commit
3. Phase 3 (US1) → MVP demo to stakeholder
4. Phases 4–7 in parallel (US2..US5 all reuse US1's hook + worker patterns)
5. Phase 8 (US6 — provider outage resilience)
6. Phase 9 (US7 — allowlist guard)
7. Phase 10 (US8 — predicate matrix)
8. Phase 11 (polish)

**Definition of done**: Phase 11 T086 — the full E2E suite is personally executed and green. Per repo memory rule, structural readiness / type-check success / partial runs do NOT count.

## Total: 86 tasks across 11 phases

| Phase | Range | Count | Story label |
|---|---|---|---|
| 1 Setup | T001–T008 | 8 | — |
| 2 Foundational | T009–T029 | 21 | — |
| 3 US1 | T030–T043 | 14 | US1 |
| 4 US2 | T044–T048 | 5 | US2 |
| 5 US3 | T049–T055 | 7 | US3 |
| 6 US4 | T056–T060 | 5 | US4 |
| 7 US5 | T061–T065 | 5 | US5 |
| 8 US6 | T066–T071 | 6 | US6 |
| 9 US7 | T072–T077 | 6 | US7 |
| 10 US8 | T078–T080 | 3 | US8 |
| 11 Polish | T081–T086 | 6 | — |

**Parallel-friendly**: 28 tasks tagged `[P]`. Setup T002+T003+T008 ($\approx 3$ in parallel), Foundational T010–T016 + T023–T025 + T028 ($\approx 11$ in parallel), each user-story phase has 2–4 [P] template/view tasks plus an [P] unit test.
