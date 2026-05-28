# Quickstart: Post-Resolution Email Notifications

**Date**: 2026-05-27

How to build, run, and verify the 12 post-resolution notification events.

## Build

```bash
dotnet build FundingPlatform.slnx
```

## Run (dev, with smtp4dev capture sidecar)

```bash
dotnet run --project src/FundingPlatform.AppHost
```

The Aspire dashboard exposes the `smtp4dev` sidecar (SMTP + HTTP REST). All outbound mail in Local is captured there; the `EmailDispatchWorker` polls the outbox every `Notifications:Worker:PollIntervalSeconds` (default 5 s). Seed users (`applicant@`, `reviewer@`, `demo-admin@programa-semilla.test`, all `Demo123!`) are allowlisted by the default `Notifications:NonProdAllowlist`.

## Manual verification walk (per user story)

**US1 — applicant response → reviewer:**
1. As applicant, open a `Resolved` application's response screen, submit accept/reject decisions.
2. In smtp4dev, confirm each group reviewer received `El solicitante respondió la resolución — Solicitud #{id}` with a `/Review/{id}` link; the applicant received nothing.

**US2 — appeal lifecycle:**
1. As applicant, open an appeal on a response with ≥1 rejected item → reviewers get `Nueva apelación abierta`.
2. Post an applicant message → reviewers get `Nuevo mensaje en la apelación`. As reviewer, post a reply → applicant gets `Nuevo mensaje del revisor…`.
3. As reviewer, resolve the appeal → applicant gets `Resolución de tu apelación` (body matches the outcome). If `GrantReopenToReview`, reviewers also get `Apelación concedida: solicitud reabierta para revisión`.

**US3 — signing ceremony:**
1. As reviewer, generate the convenio → applicant gets `Tu convenio está listo para firmar`; confirm an `AgreementGenerated` row appears in the application's version history.
2. As applicant, upload the signed PDF → reviewers get `Convenio firmado recibido para revisión` (`/Review/SigningInbox`). Replace / withdraw → reviewers get the replaced / retirado variants.
3. As reviewer, approve → applicant gets `Tu convenio fue ejecutado`. (Reject variant → applicant gets `Tu convenio firmado requiere cambios`.)

## Automated verification

```bash
# Unit (enum mappings, binding completeness)
dotnet test tests/FundingPlatform.Tests.Unit

# Integration (real DB): recipient matrix, idempotency (dual-fire + successive messages), allowlist fail-closed
dotnet test tests/FundingPlatform.Tests.Integration

# E2E (full Aspire stack + smtp4dev): one class per user story — REQUIRED green for delivery
dotnet test tests/FundingPlatform.Tests.E2E
```

## Delivery gate (per CLAUDE.md)

A feature is **not delivered** until the **full E2E suite has been personally executed and is green**. The three new E2E classes (`ResponseNotificationsTests`, `AppealNotificationsTests`, `SigningNotificationsTests`) drive the real UI journey (no deep-link shortcuts) and assert captures via `MailCaptureClient`. Structural readiness / partial runs do not count.

## Acceptance-to-test mapping

| Success criterion | Verified by |
|---|---|
| SC-001 (12 events fire, captured) | 3 E2E classes (one per US) |
| SC-002 (recipient matrix exact) | integration recipient-matrix test (applicant + reviewers-in-group + participating admin + non-participating admin) |
| SC-003 (idempotency incl. dual-fire + successive messages) | integration double-pass test |
| SC-004 (allowlist fail-closed) | integration test, empty allowlist |
| SC-005 (brand-grep green on 24 templates) | CI grep gate over `src/FundingPlatform.Web/Views/Emails/**/*.cshtml` |
| SC-006 (zero migrations / schema unchanged) | CI grep over `**/Migrations/**` + dacpac diff |
| SC-007 (reported bug closed) | US1 E2E (reviewer notified on applicant accept) |
| SC-008 (P95 < 30 s, no regression) | aggregate `NotificationDelivery.SentAt − NotificationOutbox.CreatedAt` across the E2E run |
