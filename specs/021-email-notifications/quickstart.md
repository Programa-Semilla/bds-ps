# Quickstart: Email Notifications

**For**: Developers and QA on the FundingPlatform project.
**Spec**: [spec.md](./spec.md)

## TL;DR

```bash
# Run the platform with notifications wired
dotnet run --project src/FundingPlatform.AppHost

# Open the smtp4dev capture UI (port chosen by Aspire — see AppHost console output)
xdg-open http://localhost:<smtp4dev-http-port>

# Submit, send back, resubmit, finalize — every transition shows up as a captured email in the UI.
```

## What boots when you run AppHost

| Resource | Image / Project | Default endpoint | Purpose |
|---|---|---|---|
| `sql` | SQL Server 2022 container | random | Application DB |
| `azurite` | Azurite container | random | Blob storage (spec 014) |
| `smtp4dev` | `rnwood/smtp4dev:latest` | http: random ; smtp: random | **New (spec 021)** — captures outbound mail |
| `web` | `FundingPlatform.Web` | https: random | The ASP.NET MVC app |

The Web project consumes the smtp4dev `smtp` endpoint via Aspire service-discovery env vars and posts mail to it for every workflow event.

## Verify a captured email end-to-end

1. Start AppHost.
2. Sign in as `admin@FundingPlatform.com` / configured admin password. Create an applicant account if one does not exist. Submit an application from the applicant UI.
3. Open the smtp4dev UI at the resolved HTTP endpoint (printed in AppHost startup log). You should see exactly:
   - One applicant-variant email subject `Recibimos tu solicitud — Solicitud #N`.
   - One reviewer-variant email per reviewer in the intake stage's assigned group, subject `Nueva solicitud para revisar: <Applicant Name>`.
4. Click the CTA in each captured email. The applicant email opens `/Application/Details/N`; the reviewer email opens `/Review/N`.

## Flip the provider for Mailgun

In `appsettings.Development.json` (or env vars):

```jsonc
{
  "Notifications": {
    "Provider": "Mailgun",
    "Mailgun": {
      "ApiKey":  "<key>",
      "Domain":  "mg.programa-semilla.cr",
      "BaseUrl": "https://api.mailgun.net/v3"
    },
    "Sender": {
      "Email": "dev-no-reply@programa-semilla.cr",
      "Name":  "Programa Semilla / Sistema de Banca para el Desarrollo"
    },
    "NonProdAllowlist": [
      "@programa-semilla.test",
      "qa-user@programa-semilla.cr"
    ],
    "BaseUrl": "https://localhost:7042"
  }
}
```

In Development the `RecipientAllowlistFilter` wraps the sender. Any recipient outside `NonProdAllowlist` is dropped and recorded as `BlockedByAllowlist`. **An empty allowlist drops everyone — fail-closed.** Production bypasses the filter entirely.

## Run the notification E2E suite

```bash
# Full suite (includes spec 021 notifications)
dotnet test tests/FundingPlatform.Tests.E2E

# Just the notification tests
dotnet test tests/FundingPlatform.Tests.E2E --filter "FullyQualifiedName~Notifications"

# Provider-outage resilience only (longest-running test in the suite — ~2 minutes)
dotnet test tests/FundingPlatform.Tests.E2E --filter "FullyQualifiedName~ProviderOutageResilienceTests"
```

The fixture starts AppHost with `--EphemeralStorage=true`, which:
- Boots a fresh SQL Server container per fixture run.
- Deploys the dacpac via `sqlpackage` (creates `dbo.NotificationOutbox` + `dbo.NotificationDelivery` on first run).
- Sets the sentinel admin password to `Sentinel123!`.
- Starts the smtp4dev sidecar; `MailCaptureClient` is wired and exposed on `fixture.MailCapture`.

## Inspect the outbox / delivery tables

```sql
-- Pending or in-flight rows
SELECT TOP 50 *
FROM dbo.NotificationOutbox
WHERE Status IN ('Pending','Dispatching')
ORDER BY CreatedAt DESC;

-- Failures
SELECT TOP 50 o.Id, o.EventType, o.AttemptCount, o.LastError, d.RecipientEmail, d.Status, d.LastError
FROM dbo.NotificationOutbox o
LEFT JOIN dbo.NotificationDelivery d ON d.OutboxId = o.Id
WHERE o.Status = 'DeadLetter' OR d.Status IN ('Failed','DeadLetter')
ORDER BY o.CreatedAt DESC;

-- Per-application history
SELECT * FROM dbo.NotificationOutbox WHERE ApplicationId = @id ORDER BY CreatedAt DESC;
SELECT * FROM dbo.NotificationDelivery WHERE ApplicationId = @id ORDER BY Id DESC;
```

## Tune the worker

| Knob | Default | Notes |
|---|---|---|
| `Notifications:Worker:PollIntervalSeconds` | `5` | Lower for faster dispatch; higher to reduce DB load. |
| `Notifications:Worker:MaxAttempts` | `3` | Transient retries before dead-letter. |
| `Notifications:Worker:BatchSize` | `25` | Rows pulled per poll. |

## Common failure modes

| Symptom | Likely cause | Fix |
|---|---|---|
| Email count is 0 in smtp4dev but rows exist in `NotificationOutbox` | sidecar container failed to start | Check `dotnet run --project src/FundingPlatform.AppHost` logs — `smtp4dev` resource should be Healthy. NoOpEmailSender kicks in when sidecar is down (NFR-007). |
| Row stuck at `Status=Dispatching` for >5 minutes | worker process restart mid-dispatch | Verify `NextAttemptAt` has advanced; the next poll re-claims (EC-015). |
| `Status=BlockedByAllowlist` rows piling up | empty or wrong `NonProdAllowlist` | Add the test recipient or domain to `Notifications:NonProdAllowlist`. Production-by-mistake check: confirm `HostEnvironment != "Production"`. |
| Subject template renders `Solicitud #` (no number) | `Application.Id` not yet assigned at outbox-write time | Should not happen — `SaveChangesAsync()` assigns the IDENTITY on insert; the outbox enqueue must occur AFTER `Application.AddVersionHistory(...)` AND the `_applicationRepository.UpdateAsync(application)` call but BEFORE `SaveChangesAsync()`. |

## Sender display verification

Every captured email MUST show:

- **From**: `Programa Semilla / Sistema de Banca para el Desarrollo <no-reply@programa-semilla.cr>` (or the configured sender)
- **Footer**: `Para soporte: soporte@programa-semilla.cr` plus the spec-019 signature block
- **No inline `<img>` tag** anywhere in the HTML body
- **No `"Capital Semilla"` or `"Forge"` strings** anywhere in subject / body / from / footer (brand-grep gate T030 stays green)
