# Implementation Plan: Post-Resolution Email Notifications

**Branch**: `028-post-resolution-notifications` | **Date**: 2026-05-27 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `specs/028-post-resolution-notifications/spec.md`

## Summary

Extend the shipped spec-021 email-notification subsystem with **twelve** new `NotificationEvent` values covering every applicant↔reviewer interaction after an application reaches `Resolved` (applicant-response, the full appeal lifecycle, and the convenio signing ceremony). The work is purely additive: it reuses the existing transactional-outbox → `EmailDispatchWorker` → `IEmailSender` → `RecipientAllowlistFilter` pipeline, the recipient resolver, the allowlist guard, and `_EmailLayout.cshtml`. Each event needs (1) an enum member + storage-string mappings, (2) a `NotificationTemplateBindings` entry, (3) recipient-bucket switch arms, (4) an HTML + text Razor partial pair, and (5) one `EnqueueAsync` call wired into the triggering Application-layer service. Three services (`ApplicantResponseService`, `SignedUploadService`, `FundingAgreementService`) gain an `INotificationOutboxWriter` dependency and the canonical two-phase enqueue. Two cross-cutting extensions are required: **event-aware CTA resolution** (today's CTA is bucket-only, hard-wired to two routes) and **actor exclusion** (so an actor who is also a participating admin never receives a copy of their own action). The single non-notification behavior change is an `Action="AgreementGenerated"` `VersionHistory` row appended (via the domain method) during convenio generation, so the idempotency anchor is uniform. **No schema change, no dacpac change, no EF migration** — `EventType` is already `varchar(64)`.

## Technical Context

**Language/Version**: C# 13 / .NET 10.0
**Primary Dependencies**: ASP.NET MVC, EF Core 10, ASP.NET Identity, .NET Aspire, MailKit v3 (SMTP path, existing), Razor (email templates), smtp4dev sidecar (Local capture, existing)
**Storage**: SQL Server via dacpac (`FundingPlatform.Database`). Reuses existing `dbo.NotificationOutbox` / `dbo.NotificationDelivery` tables **unchanged** (`EventType VARCHAR(64)`).
**Testing**: NUnit + Playwright E2E against the Aspire stack; integration tests against a real DB; `MailCaptureClient` against the smtp4dev REST API
**Target Platform**: Linux server (Aspire-orchestrated)
**Project Type**: Web application (ASP.NET MVC, Clean Architecture: Domain / Application / Infrastructure / Web)
**Performance Goals**: P95 time-to-send < 30 s, P99 < 2 min (inherited spec-021 NFR-002; must not regress)
**Constraints**: es-CR only; no inline `<img>`; brand-grep gate green; no PII beyond in-app access; zero EF migrations / zero dacpac change
**Scale/Scope**: 12 new events; 24 new Razor partials; 9 enqueue call sites across 3 services; 1 audit-row addition; 2 cross-cutting extensions (event-aware CTA, actor exclusion); 3 E2E classes (one per user story) + recipient/idempotency integration tests

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Evidence |
|---|---|---|
| **I. Clean Architecture** | PASS | Enum in Domain; `INotificationOutboxWriter` + bindings + payload in Application; resolver + EF in Infrastructure; partials + CTA renderer in Web. Dependencies point inward; enqueue calls live in Application services. |
| **II. Rich Domain Model** | PASS | The `AgreementGenerated` audit row is appended through `Application.AddVersionHistory` (domain method), not a raw service mutation (FR-010). State transitions remain on the aggregate. |
| **III. E2E (non-negotiable)** | PASS | One Playwright E2E per user story, driving the real UI through the smtp4dev sidecar (SC-001); recipient + idempotency integration tests against a real DB. |
| **IV. Schema-First DB** | PASS | No new tables, no `.sql` change, no EF migration. `EventType` is `varchar(64)`; enum extension stores identically (SC-006). Verified against `dbo.NotificationOutbox.sql` + `NotificationOutboxConfiguration`. |
| **V. Specification-Driven** | PASS | spec → plan → tasks → implement; US1/US2/US3 independently testable and deliverable. |
| **VI. Simplicity / YAGNI** | PASS | Reuses the entire spec-021 pipeline; no new infrastructure. Declines self-confirmations, digests, OQ-011 fix — deferred explicitly. |

**Result: PASS — no violations. Complexity Tracking not required.**

## Project Structure

### Documentation (this feature)

```text
specs/028-post-resolution-notifications/
├── plan.md              # This file
├── research.md          # Phase 0 — design decisions (CTA, actor exclusion, message direction, outcome body, two-phase save)
├── data-model.md        # Phase 1 — enum extension, payload fields, NO schema change
├── quickstart.md        # Phase 1 — how to run/verify
├── contracts/
│   └── notification-events.md   # Phase 1 — the 12-event contract: triggers, recipients, subjects, CTAs, template bindings
├── spec.md              # Approved spec
├── REVIEW-SPEC.md       # Spec-review gate (SOUND)
├── checklists/requirements.md
└── tasks.md             # Phase 2 — created by /speckit-tasks (NOT here)
```

### Source Code (touched paths)

```text
src/FundingPlatform.Domain/
└── Notifications/NotificationEvent.cs                 # +12 enum members, +12 ToStorageString / FromStorageString arms

src/FundingPlatform.Application/
├── Notifications/Templates/NotificationTemplateBindings.cs   # +12 Binding entries (subject + view names + variant key + NEW CtaRouteTemplate)
├── Notifications/NotificationPayload.cs                      # + ActorUserId field (actor exclusion); reuse OutcomeCode for appeal resolution
└── Services/
    ├── ApplicantResponseService.cs       # inject INotificationOutboxWriter; enqueue in SubmitResponseAsync, OpenAppealAsync, PostMessageAsync (directional), ResolveAppealAsync (+dual-fire)
    ├── SignedUploadService.cs            # inject writer; enqueue in UploadAsync, ReplaceAsync, WithdrawAsync, ApproveAsync, RejectAsync
    └── FundingAgreementService.cs        # inject writer; AddVersionHistory("AgreementGenerated") + enqueue in PersistGenerationAsync

src/FundingPlatform.Infrastructure/
└── Notifications/Resolvers/NotificationRecipientResolver.cs  # +12 events into IncludesApplicantBucket / IncludesReviewerBucket / IncludesAdminBucket; actor-exclusion filter
    (+ RazorEmailRenderer CTA composition → event-aware, driven by CtaRouteTemplate)

src/FundingPlatform.Web/
└── Views/Emails/                          # +24 partials: {Event}.cshtml + {Event}.text.cshtml for all 12 events

tests/FundingPlatform.Tests.E2E/
├── Notifications/ (or Brand/)             # +3 E2E classes: ResponseNotificationsTests, AppealNotificationsTests, SigningNotificationsTests
└── Pages/                                 # extend/confirm ApplicantResponsePage, AppealThreadPage, FundingAgreementPanelPage POMs

tests/FundingPlatform.Tests.Integration/
└── Notifications/                         # recipient-matrix + idempotency (dual-fire, successive messages) + allowlist tests
```

**Structure Decision**: Existing Clean-Architecture .NET solution (`FundingPlatform.slnx`). No new projects, no new top-level directories. All twelve events thread through the same five integration seams documented in `research.md`; the only structural additions are 24 Razor partials and the test classes.

## Complexity Tracking

> No Constitution violations. Section intentionally empty.
