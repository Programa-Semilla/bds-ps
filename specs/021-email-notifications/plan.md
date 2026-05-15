# Implementation Plan: Email Notifications System

**Branch**: `feature/notifications` | **Date**: 2026-05-12 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/021-email-notifications/spec.md`

## Summary

Introduce the first email-notification subsystem in the FundingPlatform. Transactional outbox written in the same EF `SaveChangesAsync()` as the workflow state change; hosted `BackgroundService` poller dispatches mail through an `IEmailSender` abstraction (SMTP via MailKit v3 → smtp4dev sidecar in Local; raw `HttpClient` → Mailgun outside Local; `NoOpEmailSender` fail-fast in Production). `RecipientAllowlistFilter` decorator wraps the sender in every non-Production environment (fail-closed empty allowlist). Two new `.sql` tables in the dacpac (`NotificationOutbox`, `NotificationDelivery`). Six enum values cover the v1 event catalog — `APPLICATION_SUBMITTED` is split into `_REVIEWER` / `_APPLICANT` for clean idempotency-key dedup. Razor templates under one shared `_EmailLayout.cshtml` with HTML + plain-text fallback; text-only spec-019 wordmark; static `mailto:soporte@programa-semilla.cr` footer. Deferred-counterpart placeholder test `EmailTemplateSenderTests.Assert.Ignore` is removed and replaced with real `MailCaptureClient` assertions exposed through `AspireFixture`.

## Technical Context

**Language/Version**: C# 13 / .NET 10.0
**Primary Dependencies**: ASP.NET MVC, EF Core 10, ASP.NET Identity, .NET Aspire, Razor view engine, **MailKit v3 (MIT) — NEW**
**Storage**: SQL Server (Aspire-managed container in dev; dacpac is schema source-of-truth). Two new tables: `dbo.NotificationOutbox`, `dbo.NotificationDelivery`. Zero EF migrations.
**Testing**: NUnit + Playwright for E2E (`tests/FundingPlatform.Tests.E2E`); xUnit-style integration tests with real DB (`tests/FundingPlatform.Tests.Integration`).
**Target Platform**: Linux server (production), Linux dev workstation (Aspire). Mail provider runtime: smtp4dev container in Local, Mailgun HTTP API in Staging/Production.
**Project Type**: ASP.NET MVC monolith with Clean Architecture layers (Domain / Application / Infrastructure / Web / Database / AppHost / ServiceDefaults).
**Performance Goals**: P95 time-to-send (outbox `CreatedAt` → delivery `SentAt`) under 30 s under normal load; P99 under 2 minutes during retry cycles. "Normal load" pinned as: the load produced by a full E2E suite run with `Notifications:Worker:PollIntervalSeconds=5`.
**Constraints**: No inline `<img>` in any template (spec-019 NFR-005 / brand-grep gate T030). No EF migrations (constitution §IV). es-CR Spanish only. No new MVC routes. No `List-Unsubscribe` header in v1.
**Scale/Scope**: v1 — 6 event enum values, 8 user stories, 2 new tables, ~9 Razor partials (8 body variants + 1 layout), 1 hosted `BackgroundService`, 3 `IEmailSender` implementations, 1 decorator (`RecipientAllowlistFilter`), 1 sidecar container (smtp4dev). Single-replica worker.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Notes |
|---|---|---|
| §I Clean Architecture | ✅ PASS | Workflow transition stays on `Application` aggregate (Domain). Outbox writer + `INotificationRecipientResolver` + `IEmailSender` interface in Application. EF mapping + `MailtrapSmtpEmailSender` + `MailgunHttpEmailSender` + `NoOpEmailSender` + `RecipientAllowlistFilter` + `EmailDispatchWorker` in Infrastructure. Razor templates in Web. Dependencies point inward. |
| §II Rich Domain Model | ✅ PASS | Workflow transitions (`Submit`, `SendBack`, `Finalize`) remain on `Application`. The outbox row is enqueued from the Application Service in the same `SaveChangesAsync()` — not from a controller. Recipient resolution + provider dispatch live outside the aggregate. |
| §III E2E Mandatory (non-negotiable) | ✅ PASS | FR-031 / FR-032 / SC-001 / SC-005 mandate one E2E per event variant against the smtp4dev sidecar. `EmailTemplateSenderTests.Assert.Ignore` is removed and replaced. `AspireFixture` is extended with `MailCaptureClient`. |
| §IV Schema-First (Dacpac) | ✅ PASS | `dbo.NotificationOutbox.sql` and `dbo.NotificationDelivery.sql` are added to `FundingPlatform.Database/Tables/`. EF Core Code-First mapping is used for data access only. No EF migrations. CI grep gate `**/Migrations/**` stays green. |
| §V Specification-Driven Development | ✅ PASS | 8 priority-ordered, independently testable user stories. Spec → Plan → Tasks → Implementation lineage. Open questions tracked in §Open Questions; 5 resolved this session, 5 remain (4 planning-pin / 1 deferred). |
| §VI Simplicity / YAGNI | ✅ PASS | No domain-event dispatcher abstraction. No i18n key system (es-CR only). No multi-replica worker tuning. No in-app channel. No bounce-webhook ingestion. Each rejected complexity is logged with rationale in `implementation-notes.md` and §Out of Scope. |

**Verdict:** No violations. `Complexity Tracking` table left empty.

## Project Structure

### Documentation (this feature)

```text
specs/021-email-notifications/
├── plan.md              # This file (/speckit-plan command output)
├── spec.md              # Feature specification (clarified 2026-05-12)
├── research.md          # Phase 0 output (this stage)
├── data-model.md        # Phase 1 output (this stage)
├── contracts/
│   ├── IEmailSender.md
│   ├── INotificationRecipientResolver.md
│   └── MailCaptureClient.md
├── quickstart.md        # Phase 1 output (this stage)
├── REVIEW-SPEC.md       # Spec review + re-review (2026-05-11 / 2026-05-12)
├── review_brief.md
├── implementation-notes.md
├── checklists/
└── tasks.md             # Phase 2 output (/speckit-tasks — NOT this command)
```

### Source Code (repository root)

```text
src/
├── FundingPlatform.AppHost/
│   └── AppHost.cs                                                          # ADD smtp4dev container resource (FR-030)
│
├── FundingPlatform.Domain/
│   └── Notifications/
│       └── NotificationEvent.cs                                             # NEW enum (6 values)
│
├── FundingPlatform.Application/
│   └── Notifications/
│       ├── IEmailSender.cs                                                  # NEW interface
│       ├── INotificationRecipientResolver.cs                                # NEW interface
│       ├── INotificationOutboxWriter.cs                                     # NEW interface (transactional enqueue)
│       ├── NotificationRecipient.cs                                         # NEW value object
│       ├── NotificationPayload.cs                                           # NEW DTO (PayloadJson shape)
│       └── Templates/
│           └── NotificationTemplateBindings.cs                              # NEW (subject + variant key map)
│
├── FundingPlatform.Infrastructure/
│   ├── Notifications/
│   │   ├── Persistence/
│   │   │   ├── NotificationOutbox.cs                                        # NEW entity (EF-mapped)
│   │   │   ├── NotificationDelivery.cs                                      # NEW entity (EF-mapped)
│   │   │   ├── NotificationOutboxConfiguration.cs                           # NEW EF IEntityTypeConfiguration
│   │   │   ├── NotificationDeliveryConfiguration.cs                         # NEW EF IEntityTypeConfiguration
│   │   │   └── NotificationOutboxWriter.cs                                  # NEW (impl of INotificationOutboxWriter via FundingPlatformDbContext)
│   │   ├── Providers/
│   │   │   ├── MailtrapSmtpEmailSender.cs                                   # NEW (MailKit v3, SMTP path)
│   │   │   ├── MailgunHttpEmailSender.cs                                    # NEW (raw HttpClient → Mailgun)
│   │   │   └── NoOpEmailSender.cs                                           # NEW (logs + returns success)
│   │   ├── RecipientAllowlistFilter.cs                                      # NEW IEmailSender decorator
│   │   ├── Resolvers/
│   │   │   ├── NotificationRecipientResolver.cs                             # NEW (composes per-event predicate)
│   │   │   └── ParticipatingAdminPredicate.cs                               # NEW (queries VersionHistory + AdminAuditEvent)
│   │   ├── Templating/
│   │   │   └── RazorEmailRenderer.cs                                        # NEW (renders Views/Emails/* off-thread)
│   │   ├── Workers/
│   │   │   └── EmailDispatchWorker.cs                                       # NEW BackgroundService (poller + retry/backoff)
│   │   └── DependencyInjection/
│   │       └── NotificationsServiceCollectionExtensions.cs                  # NEW (AddNotifications)
│   └── Persistence/
│       └── FundingPlatformDbContext.cs                                      # EDIT — register DbSet<NotificationOutbox>, DbSet<NotificationDelivery>
│
├── FundingPlatform.Web/
│   ├── Views/
│   │   └── Emails/
│   │       ├── _EmailLayout.cshtml                                          # NEW shared layout (header/footer/CTA styles)
│   │       ├── _SupportFooter.cshtml                                        # NEW partial (mailto:soporte@…)
│   │       ├── ApplicationSubmittedApplicant.cshtml                         # NEW body variant (HTML)
│   │       ├── ApplicationSubmittedApplicant.text.cshtml                    # NEW body variant (plain text)
│   │       ├── ApplicationSubmittedReviewer.cshtml
│   │       ├── ApplicationSubmittedReviewer.text.cshtml
│   │       ├── ReturnedToApplicant.cshtml
│   │       ├── ReturnedToApplicant.text.cshtml
│   │       ├── ResubmittedByApplicant.cshtml
│   │       ├── ResubmittedByApplicant.text.cshtml
│   │       ├── ApplicationApproved.cshtml
│   │       ├── ApplicationApproved.text.cshtml
│   │       ├── ApplicationRejected.cshtml
│   │       └── ApplicationRejected.text.cshtml
│   ├── Controllers/
│   │   ├── ApplicationController.cs                                          # EDIT — `ApplicationService.SubmitApplicationAsync()` calls outbox writer (Application layer change cascades here only as a verification touchpoint)
│   │   └── ReviewController.cs                                               # EDIT — `ReviewService.SendBackAsync()` + `FinalizeAsync()` call outbox writer
│   └── Program.cs                                                            # EDIT — call AddNotifications() during service registration
│
├── FundingPlatform.Application/
│   └── ApplicationServices/                                                  # (existing folder; expand if needed)
│       ├── ApplicationService.cs                                             # EDIT — enqueue outbox row(s) inside SubmitApplicationAsync / SendBackAsync
│       └── ReviewService.cs                                                  # EDIT — enqueue outbox row inside FinalizeAsync / SendBackAsync
│
└── FundingPlatform.Database/
    └── Tables/
        ├── dbo.NotificationOutbox.sql                                        # NEW table .sql
        └── dbo.NotificationDelivery.sql                                      # NEW table .sql (carries the idempotency UNIQUE INDEX)

tests/
├── FundingPlatform.Tests.Unit/
│   └── Notifications/
│       ├── RecipientAllowlistFilterTests.cs                                  # NEW
│       ├── NotificationRecipientResolverTests.cs                             # NEW (in-memory predicate validation)
│       ├── EmailDispatchWorkerTests.cs                                       # NEW (backoff math, claim-lose semantics)
│       └── RazorEmailRendererTests.cs                                        # NEW (no-img, brand-grep, es-CR)
│
├── FundingPlatform.Tests.Integration/
│   └── Notifications/
│       ├── OutboxTransactionalEnqueueTests.cs                                # NEW — fail the transaction → zero outbox rows
│       ├── ParticipatingAdminPredicateTests.cs                               # NEW — role-change matrix per US8
│       ├── IdempotencyDoubleProcessTests.cs                                  # NEW — SC-003
│       ├── AllowlistFailClosedTests.cs                                       # NEW — SC-004 / US7
│       └── DeadLetterPathTests.cs                                            # NEW — permanent failure → DeadLetter
│
└── FundingPlatform.Tests.E2E/
    ├── Fixtures/
    │   ├── AspireFixture.cs                                                  # EDIT — start smtp4dev resource, expose `MailCaptureClient`
    │   └── MailCaptureClient.cs                                              # NEW — HTTP client wrapping smtp4dev REST API
    ├── Notifications/
    │   ├── ApplicationSubmittedNotificationsTests.cs                         # NEW — US1
    │   ├── ReturnedToApplicantNotificationsTests.cs                          # NEW — US2
    │   ├── ResubmittedNotificationsTests.cs                                  # NEW — US3
    │   ├── ApprovedAndRejectedNotificationsTests.cs                          # NEW — US4 + US5
    │   ├── ProviderOutageResilienceTests.cs                                  # NEW — US6 (sidecar SIGSTOP/SIGCONT)
    │   └── AllowlistGuardE2ETests.cs                                         # NEW — US7
    └── Brand/
        └── EmailTemplateSenderTests.cs                                        # EDIT — replace `Assert.Ignore` with real send/capture assertions (FR-032)
```

**Structure Decision**: Single-solution Clean Architecture monolith (matches the existing FundingPlatform layout). The notifications subsystem is layered across Domain / Application / Infrastructure / Web in the same projects already on disk; no new project is introduced. The dacpac receives two new `.sql` files. The AppHost gains one new container resource (smtp4dev). The E2E fixture gains one new client surface (`MailCaptureClient`).

## Complexity Tracking

> No constitution violations. Section intentionally empty.

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| _(none)_ |  |  |

---

## Phase 0: Outline & Research

See [research.md](./research.md) for the consolidated findings. Highlights:

- **R-001** — `Application.Folio` does not exist. Subject templates use `Solicitud #{Application.Id}`. _Resolves OQ-007._
- **R-002** — Workflow hook point: `ApplicationService.SubmitApplicationAsync` and `ReviewService.SendBackAsync` / `FinalizeAsync` follow the pattern `mutate → AddVersionHistory → enqueue outbox via DbContext → SaveChangesAsync`. Atomic.
- **R-003** — No domain-level `Resubmit()` method exists. Resubmission is `Application.Submit()` invoked after a prior `SendBack()`. The outbox writer selects `RESUBMITTED_BY_APPLICANT` vs `APPLICATION_SUBMITTED_REVIEWER` by querying `VersionHistory` for a prior `Action="SendBack"` row on the same `ApplicationId`. _Spec evolved 2026-05-12 — §Event Catalog Trigger column updated, §Assumptions corrected._
- **R-004** — Application-level final outcome is derived from `Application.Finalize(force)` + per-item `Item.Approve` / `Item.Reject` decisions. The outbox writer fires `APPLICATION_APPROVED` if every required item is `Approved` post-Finalize; otherwise `APPLICATION_REJECTED`. _Spec evolved — §Event Catalog + §Dependencies updated._
- **R-005** — MVC routes: applicant detail is `/Application/Details/{id}` (singular `Application`); reviewer detail is `/Review/{id}` (via `ReviewRoutes.ReviewTemplate`). _Spec FR-026 evolved to match codebase._
- **R-006** — Participating-admin predicate: `SELECT DISTINCT vh.UserId FROM VersionHistory vh WHERE vh.ApplicationId = @id` filtered by `IsInRoleAsync(userId, "Admin")` _at the time of resolver invocation_ (current role). No new admin-audit infrastructure. `AdminAuditEvent` is queried only for actions whose `TargetType='application' AND TargetId=@id` — none exist today, so v1 the participating-admin source is `VersionHistory.UserId`.
- **R-007** — Aspire smtp4dev wiring uses `builder.AddContainer("smtp4dev", "rnwood/smtp4dev:latest").WithHttpEndpoint(...).WithEndpoint(..., protocol: "tcp", name: "smtp")`. The web app `WithReference(smtp).WaitFor(smtp)`. The E2E fixture extracts the http endpoint via `_app.GetEndpoint("smtp4dev", "http")` to wire `MailCaptureClient`.
- **R-008** — `EmailTemplateSenderTests.Assert.Ignore` placeholder file is quoted verbatim in `research.md` (will be wholly replaced — preserving namespace and class name to avoid breaking test-explorer references).
- **R-009** — Dacpac table pattern: see `dbo.Documents.sql` example in `research.md` for the canonical column-formatting style.

**NEEDS CLARIFICATION:** none after R-001..R-009.

## Phase 1: Design & Contracts

**Prerequisites:** research.md complete (✅).

### Outputs

- **[data-model.md](./data-model.md)** — `NotificationOutbox`, `NotificationDelivery`, `NotificationEvent` (enum), `NotificationRecipient` value object, EF mapping notes, indexes, constraints.
- **[contracts/IEmailSender.md](./contracts/IEmailSender.md)** — Sender interface, request/response shape, error semantics (transient vs permanent), retries, observability.
- **[contracts/INotificationRecipientResolver.md](./contracts/INotificationRecipientResolver.md)** — Resolver interface, per-event resolution rules, dedup + bucket-priority semantics.
- **[contracts/MailCaptureClient.md](./contracts/MailCaptureClient.md)** — Test-facing client over smtp4dev REST API; drain, list, assert helpers.
- **[quickstart.md](./quickstart.md)** — How to run the stack with notifications enabled (developer onboarding); how to verify a captured email; how to flip providers.

### Agent context update

`CLAUDE.md` already references the active plan via the `Active Technologies` / `Recent Changes` sections. The `<!-- SPECKIT START -->` / `<!-- SPECKIT END -->` block does not exist in this repo (verified by grep). I am updating the `Active Technologies` table and `Recent Changes` list inline at the end of this plan stage to point to `specs/021-email-notifications/plan.md`.

## Re-evaluation: Constitution Check Post-Design

| Principle | Status | Notes |
|---|---|---|
| §I Clean Architecture | ✅ PASS | Verified by Project Structure tree — no Domain → Infrastructure references; no Web → Domain mutations; resolver and outbox writer live in Application layer. |
| §II Rich Domain Model | ✅ PASS | `Application.Submit/SendBack/Finalize` unchanged. Outbox enqueue is done from Application Services, not from `Application` aggregate methods (the aggregate stays storage-agnostic). |
| §III E2E Mandatory | ✅ PASS | One E2E per user story; `MailCaptureClient` is the seam. |
| §IV Schema-First | ✅ PASS | `.sql` files only; no migrations. |
| §V SDD | ✅ PASS | Plan ratifies the spec; all FRs are addressed by a project-structure entry. |
| §VI Simplicity | ✅ PASS | No deferred-feature creep — see §Out of Scope in spec. |

**Verdict:** Plan ready for `/speckit-tasks`.
