# Phase 0 Research: Email Notifications System

**Date**: 2026-05-12
**Source**: parallel `Explore` subagents over the FundingPlatform codebase.

This document consolidates the codebase-level discoveries made during planning. Each finding has the form `Decision / Rationale / Alternatives considered`. The plan.md references these as `R-NNN`.

---

## R-001 — Folio does not exist; numeric `Application.Id` is the canonical identifier

**Decision**: Subject templates use `Solicitud #{Application.Id}`. The `PayloadJson` snapshot carries `Application.Id` (int) directly.

**Rationale**: The `FundingPlatform.Domain.Entities.Application` aggregate (located at `src/FundingPlatform.Domain/Entities/Application.cs`) has **no `Folio` field**. The `Id` IDENTITY column on `dbo.Applications` is the only human-readable application identifier in the system today. Line codes exist at `Item.LineCode` granularity but are not application-level. Resolves OQ-007.

**Alternatives considered**:
- Add a `Folio` field on `Application` (rejected — out of v1 scope; would force a dacpac column add + backfill).
- Use applicant email or applicant display name (rejected — not unique; PII leakage in subject lines).
- Hash + truncate `Id` (rejected — adds opaqueness without benefit).

---

## R-002 — Workflow hook point is the Application Service, between `AddVersionHistory` and `SaveChangesAsync`

**Decision**: The outbox row is enqueued through the same `FundingPlatformDbContext` as the workflow mutation, inside the Application Service method that owns the unit-of-work. The single `SaveChangesAsync()` wraps both `VersionHistory` and `NotificationOutbox` inserts in one DB transaction.

**Rationale**: Read of `src/FundingPlatform.Application/.../ApplicationService.cs` line 157 shows the canonical pattern:

```csharp
application.Submit(minQuotations);
application.AddVersionHistory(new VersionHistory(userId, "Submitted", "..."));
await _applicationRepository.UpdateAsync(application);
await _applicationRepository.SaveChangesAsync(); // single tx
```

Adding `_outboxWriter.Enqueue(...)` (which calls `_context.Set<NotificationOutbox>().Add(row)`) before `SaveChangesAsync` lands the row atomically. If `application.Submit()` throws (validation), `SaveChangesAsync` is never called → no outbox row. FR-001 satisfied.

**Alternatives considered**:
- Domain-event dispatcher pattern (rejected — adds an abstraction layer not justified by current need; §VI YAGNI; documented in `implementation-notes.md`).
- EF `SaveChangesAsync` interceptor (rejected — couples outbox semantics to every save, not just workflow transitions; harder to test in isolation).
- Outbox written from a controller (rejected — violates Clean Architecture §I; controller does not know workflow context).

---

## R-003 — `Resubmit()` is not a domain method; resubmission is `Submit()` invoked after `SendBack()`

**Decision**: The outbox writer queries `VersionHistory` for any prior row with `ApplicationId=@id AND Action="SendBack"` to distinguish first-time submit from resubmit. First-time → emit `APPLICATION_SUBMITTED_REVIEWER` + `APPLICATION_SUBMITTED_APPLICANT` rows. Resubmit → emit a single `RESUBMITTED_BY_APPLICANT` row (applicant gets no email per §Recipient Rules).

**Rationale**: Research showed `Application.Submit(int minQuotations)` and `Application.SendBack()` exist, but no `Application.Resubmit()`. `SendBack()` transitions `UnderReview → Draft`. The applicant then edits and re-presses Submit. The codebase has one `Submit()` method that the outbox cannot disambiguate without inspecting history.

**Alternatives considered**:
- Add `Application.Resubmit()` domain method (rejected — out of scope; spec 002 owns the workflow vocabulary; over-fitting to notification needs).
- Snapshot prior state into `Application` (rejected — duplicate of `VersionHistory`).
- Track resubmit count on `Application` (rejected — derivable from `VersionHistory`).

---

## R-004 — Application-level final outcome is derived from `Application.Finalize(force)` + per-item statuses

**Decision**: After `Application.Finalize(force)`, the outbox writer reads the resolved application's item statuses. If every required item is `Approved` (or the application's derived outcome enum is `Approved`), emit `APPLICATION_APPROVED`. Otherwise emit `APPLICATION_REJECTED`. The exact derivation rule will be re-confirmed in the Implement stage by reading `Application.Finalize` and `Item.Status` enums; the spec contract is symmetric (FR-010 / FR-011 differ only in template variant).

**Rationale**: Research showed `Application.Finalize(bool force)` is the terminal transition (`UnderReview → Resolved`). There is no top-level `Application.Approve()` / `Application.Reject()`. Per-item `Item.Approve(int supplierId, string?)` and `Item.Reject(string?)` set item outcomes. The application's aggregate-level outcome is derived; no boolean flag exists today.

**Alternatives considered**:
- Add `Application.Outcome` enum column (rejected — out of v1 scope; derivable at write-time).
- Emit both `APPROVED` and `REJECTED` events with template variant chosen at render-time (rejected — bloats idempotency-key space; render-time branching is harder to test).

---

## R-005 — MVC route templates

**Decision**:

| Surface | Route template | Controller / Action | Authorize |
|---|---|---|---|
| Applicant detail | `/Application/Details/{id}` | `ApplicationController.Details(int id)` | `[Authorize(Roles = "Applicant")]` |
| Reviewer / admin detail | `/Review/{id}` (via `ReviewRoutes.ReviewTemplate`) | `ReviewController.Review(int id, CancellationToken ct)` | `[Authorize(Roles = "Reviewer,Admin")]` |

Spec FR-026 evolved 2026-05-12 to match these exact route templates. The previously-spec'd `/Applications/Details/{id}` (plural) and `/Reviewer/Applications/Details/{id}` (route prefix) do not exist.

**Rationale**: Reading `Program.cs` (default `{controller=Home}/{action=Index}/{id?}` pattern) + `ReviewController.cs` line 142 (`[Route(ReviewRoutes.ReviewTemplate)]`) + `ReviewRoutes.cs` (`public const string ReviewTemplate = "Review/{id:int}";`) confirmed the live templates. Access control is server-side via the existing class-level `[Authorize]` attributes; no new MVC routes are introduced.

**Alternatives considered**:
- Introduce new `/Notifications/Deep-link/{token}` redirect routes carrying signed tokens (rejected — out of v1 scope; FR-026 explicitly forbids new routes).

---

## R-006 — Participating-admin predicate sources

**Decision**: For v1, the participating-admin predicate is:

```sql
SELECT DISTINCT vh.UserId
FROM dbo.VersionHistory vh
INNER JOIN dbo.AspNetUserRoles ur ON ur.UserId = vh.UserId
INNER JOIN dbo.AspNetRoles r       ON r.Id     = ur.RoleId
WHERE vh.ApplicationId = @applicationId
  AND r.NormalizedName = 'ADMIN'
  AND vh.UserId IS NOT NULL;
```

EF Core composition equivalent. The query reads `VersionHistory` as the explicit-action source and `AspNetUserRoles` to evaluate _current_ admin role (EC-002 — demoted admin who acted still qualifies via the role join? **NO — re-read** the predicate. Demoted admin currently in reviewer role would NOT qualify under the strict role-join above.). 

**Resolution**: EC-002 requires that a demoted admin who acted in the past STAYS in the participating-admin bucket. Therefore the predicate must NOT filter by current role; instead, all `VersionHistory.UserId` for the application is the source, and the resolver labels each entry as `admin` bucket if `IsInRoleAsync(userId, "Admin")` returns true at the moment of resolution **OR** if the historical action is one that only an admin could have performed (e.g., admin-edits to the application). For v1, since admin-edits to applications are not tracked separately from reviewer actions in `VersionHistory.Action`, the predicate falls back to: any `VersionHistory.UserId` whose user is currently in the Admin role.

This is the over-narrow path: a demoted admin who acted in the past will not currently be in the Admin role and so will NOT qualify. EC-002 is therefore **only partially supported in v1** — the spec's `participating-admin` semantics for demoted users is deferred to a future spec that introduces an explicit `AdminAuditEvent` of type `application.acted` or a `VersionHistory.Role` snapshot column. _Spec evolution flagged for review._

**Alternatives considered**:
- Add `VersionHistory.RoleAtAction` snapshot column (rejected — schema change beyond v1 scope; deferred).
- Use `AdminAuditEvent` as a single source-of-truth (rejected — `AdminAuditEvent` today only tracks group / user CRUD; no application-targeted action rows exist).
- Drop EC-002 from v1 (recommended) — see plan-time follow-up below.

**Plan-time follow-up**: EC-002 is downgraded from a strict requirement to a known limitation in v1; spec to be evolved post-implement to reflect the v1 predicate behavior. Tracking under a new open question: **OQ-011 — Participating-admin predicate for role-changed users (deferred to a future spec)**.

---

## R-007 — Aspire smtp4dev container wiring

**Decision**: `src/FundingPlatform.AppHost/AppHost.cs` is amended with:

```csharp
var smtp4dev = builder.AddContainer("smtp4dev", "rnwood/smtp4dev:latest")
    .WithHttpEndpoint(port: null, targetPort: 80, name: "http")
    .WithEndpoint(port: null, targetPort: 25, scheme: "tcp", name: "smtp");

webApp
    .WithReference(smtp4dev.GetEndpoint("smtp"))
    .WithReference(smtp4dev.GetEndpoint("http"))
    .WaitFor(smtp4dev);
```

The Web project consumes:
- `services__smtp4dev__smtp__0` env var → `Notifications:Mailtrap:Host`/`Port` (Aspire injects host+port pair).
- `services__smtp4dev__http__0` env var → consumed by `MailCaptureClient` only in test fixture.

**Rationale**: Mirrors the existing `AddContainer(...)` + `WaitFor(...)` pattern used by the SQL Server resource. Aspire's service-discovery env var convention surfaces the resolved endpoint to the consumer without hard-coding ports. The dev workflow does not block if the container fails to start (NFR-007 fallback → `NoOpEmailSender`).

**Alternatives considered**:
- `AddAzureMailHog()` / community NuGet wrappers (rejected — adds managed-NuGet review burden; raw `AddContainer` is sufficient).
- Persistent volume for captured messages (rejected — sidecar is sink-only; messages are ephemeral by design).
- MailHog (Go-based) instead of smtp4dev (rejected — _resolved 2026-05-12 in clarify; OQ-002_).

---

## R-008 — Placeholder test file content (verbatim) and replacement contract

**Decision**: `tests/FundingPlatform.Tests.E2E/Brand/EmailTemplateSenderTests.cs` is rewritten end-to-end, preserving the namespace `FundingPlatform.Tests.E2E.Brand` and class name `EmailTemplateSenderTests` (so test-explorer references stay stable). The new file ships `[Test]` methods per event variant, each asserting:

1. The expected captured-mail count (one per recipient, no duplicates).
2. The exact sender display string `"Programa Semilla / Sistema de Banca para el Desarrollo"`.
3. The signature block matches.
4. No inline `<img>` in HTML body.
5. No `"Capital Semilla"` or `"Forge"` strings in any field.
6. The subject template renders correctly with `{Application.Id}` interpolation.
7. The CTA href matches the role-specific deep-link route (FR-026).

**Rationale**: FR-032 requires the `Assert.Ignore` placeholder to be removed and replaced with real `[Test]` cases. The class summary already documents the contract (quoted in the file's XML doc). Preserving the class name preserves CI test-id history; the entire body is otherwise replaced.

---

## R-009 — Dacpac table-definition pattern

**Decision**: Two new `.sql` files in `src/FundingPlatform.Database/Tables/`:

- `dbo.NotificationOutbox.sql`
- `dbo.NotificationDelivery.sql`

Both follow the columnar formatting style of `dbo.Documents.sql`:

```sql
CREATE TABLE [dbo].[TableName]
(
    [Id]      INT      IDENTITY(1,1) NOT NULL,
    -- ... aligned columns ...
    CONSTRAINT [PK_TableName] PRIMARY KEY CLUSTERED ([Id])
);
GO

CREATE INDEX [IX_TableName_Foo] ON [dbo].[TableName] (...);
```

Indexes go in the same file as the table (not separate `.sql`) per repo convention. See [data-model.md](./data-model.md) for the full DDL.

---

## Resolved vs. still-open OQs (post-research)

| OQ | Topic | Status |
|---|---|---|
| OQ-001 | Mailgun unsubscribe footer | Resolved (Clarify 2026-05-12) — static `mailto` footer |
| OQ-002 | sidecar choice | Resolved (Clarify) — smtp4dev |
| OQ-003 | real Mailtrap dev override | Still open — config knob exists; default stays sidecar |
| OQ-004 | sender email per env | Still open — pure config; document defaults in CLAUDE.md |
| OQ-005 | MailKit license | Resolved (Clarify) — v3 MIT |
| OQ-006 | SUBMITTED enum split | Resolved (Clarify) — `_REVIEWER` + `_APPLICANT` |
| OQ-007 | Folio source-of-truth | Resolved (Research R-001) — no Folio; use `Id` |
| OQ-008 | retention | Resolved (Clarify) — 90d / 1y |
| OQ-009 | multi-replica scaling | Deferred to future spec |
| OQ-010 | brand-grep gate scope | Resolved (Plan-level) — source `.cshtml` layer; CI grep `**/Views/Emails/**/*.cshtml` |
| **OQ-011** | participating-admin for role-changed users (EC-002) | **NEW — deferred to a future spec** per R-006 |

Pin date for OQ-011 to be tracked in the spec's §Open Questions after the plan checkpoint commit.

---

## Post-Implementation Findings

### FR-001 — workflow + outbox atomicity: two-phase save (deviation, not fix)

**What the spec says (FR-001 / R-002)**: the workflow-state save and the outbox-row save commit atomically, in the *same* EF Core `SaveChanges` transaction. The original design language called for `_context.Database.BeginTransactionAsync()` wrapping both operations.

**What we actually shipped**: `ApplicationService.SubmitApplicationAsync` (and the parallel `ReviewService.SendBackAsync`, `ReviewService.FinalizeReviewAsync`) call `_applicationRepository.SaveChangesAsync()` **twice** with no explicit `BeginTransaction`. The first save persists the workflow row + assigns `VersionHistory.Id`; the second save persists the outbox row(s) referencing that id.

**Why**: the Aspire-managed `Microsoft.Data.SqlClient` connection has the transient-retry execution strategy enabled (default for `AddSqlServerDbContext<>`). Wrapping the dual save in an explicit `BeginTransactionAsync` conflicts with the retry strategy — EF Core requires either `dbContext.Database.CreateExecutionStrategy().ExecuteAsync(...)` to bracket the transaction, or the retry strategy disabled, or no explicit transaction at all. The implement subagent observed silent SaveChanges failures (no thrown exception, no commit) when wrapping the two saves in `IDbContextTransaction`; switching to the two-phase pattern resolved it and the SqlServer-deployed integration + E2E tests now pass.

**Risk profile of the deviation**: between the two saves the workflow row is committed but the outbox row is not. If the process crashes there, the workflow advances without firing the notification. The worker has no compensating sweep for "workflow rows whose expected outbox row never persisted" — it polls outbox only. The exposure window is ~1 ms in practice. For the platform's traffic profile this is acceptable, but it is a real divergence from FR-001's "atomic" promise.

**Recommended follow-up (for spex-evolve)**: either (a) update FR-001 to formalize the two-phase save and document the ~1 ms exposure window, or (b) re-introduce a true single transaction via `CreateExecutionStrategy().ExecuteAsync(...)` so the retry policy and transactional save coexist correctly. Option (a) is the smaller delta; option (b) restores spec fidelity.

### T086 root cause — smtp4dev endpoint env-var resolution

The first E2E run revealed that **candidate #2** from the implementation notes was correct: `MailtrapSmtpEmailSender.ResolveEndpoint()` was falling through to its config-file fallback (`localhost:25`) because the expected Aspire service-discovery env var (`services__smtp4dev__smtp__0`) was not present at the resolved key/format. Resolved by emitting `Notifications__Mailtrap__Host` and `Notifications__Mailtrap__Port` directly in `AppHost.cs` from `smtpEndpoint.Property(EndpointProperty.Host)` / `EndpointProperty.Port` so the host/port flow through the platform's own config path. This is a robustness fix that does not change spec semantics — the SMTP destination wiring in non-prod was always supposed to point at the Aspire sidecar.

### T086 secondary fixes (test-only, no spec impact)

- `MailCaptureClient` had DTO and content-fetching bugs against smtp4dev 3.6.x: `to` is an array, not a string; part bodies are served at `/api/Messages/{id}/part/{partId}/content`, not inlined in the message detail; `FromName` is not a top-level property (display name must be parsed out of `From`).
- `ApplicationSubmittedNotificationsTests` filtered captured emails by recipient only. Because the test's `RegisterUserAsync` helper calls `AssignAllGroupsAsync`, the applicant also lands in every seeded group and therefore receives the reviewer-variant fan-out copy. Filter now also matches subject prefix.
- `ReturnedToApplicantNotificationsTests` did not register a Playwright `Page.Dialog` handler for the `confirm("¿Devolver...?")` prompt, so the form never posted. Added an `AcceptAsync` handler.
