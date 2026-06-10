# Capital Semilla / FundingPlatform

Last updated: 2026-06-09

## Stack

.NET 10.0, ASP.NET MVC, EF Core 10, ASP.NET Identity, .NET Aspire, SQL Server (Aspire-managed container), Syncfusion HtmlToPdfConverter (Linux, vendored license fallback in dev), Tabler.io vendored CSS/JS, Playwright for E2E. Solution file: `FundingPlatform.slnx`.

## Layout

```text
src/
  FundingPlatform.AppHost          Aspire orchestrator — entry point for dev and tests
  FundingPlatform.Web              ASP.NET MVC UI, controllers, views, wwwroot
  FundingPlatform.Application      Use cases, projection services (e.g. IApplicantDashboardProjection)
  FundingPlatform.Domain           Entities, aggregates, domain events
  FundingPlatform.Infrastructure   EF Core DbContext, file storage, PDF generation, Identity wiring
  FundingPlatform.Database         dacpac — schema source of truth
  FundingPlatform.ServiceDefaults  Shared Aspire defaults (telemetry, health checks)
tests/
  FundingPlatform.Tests.Unit
  FundingPlatform.Tests.Integration
  FundingPlatform.Tests.E2E        Playwright + AspireFixture
specs/                             NNN-slug/ per feature: spec.md, plan.md, tasks.md
scripts/                           Verification scripts (asset budget, tokens, pdf carve-outs, perf baselines)
brainstorm/                        Working scratchpad for in-flight design exploration
```

## Run / build

- Dev (with persistent SQL data volume + auto-deployed dacpac):
  `dotnet run --project src/FundingPlatform.AppHost`
- Build whole solution: `dotnet build FundingPlatform.slnx`
- Schema changes: edit `FundingPlatform.Database` (dacpac). AppHost auto-deploys at startup outside ephemeral mode.

## Configuration knobs (read in `AppHost.cs`)

| Key | Default | Notes |
|---|---|---|
| `EphemeralStorage` | `false` | When `true`: skip persistent SQL data volume, skip auto-deploy of the SQL project, force sentinel admin password to `Sentinel123!`. Set by E2E fixture. |
| `Syncfusion:LicenseKey` | dev fallback embedded | Override in real envs. |
| `FundingAgreement:LocaleCode` | `es-CR` | Default culture. |
| `FundingAgreement:CurrencyIsoCode` | `CRC` | Funding-agreement currency (spec 015 — was `COP` before multi-currency). |
| `SignedUpload:MaxSizeBytes` | `20971520` (20 MiB) | Signed-PDF upload cap. |
| `AdminReports:DefaultCurrency` | `CRC` | Reports currency code; also pre-fills the supplier quotation Currency input. Spec 015: must match a code in the seeded `dbo.Currencies` catalog so the conversion path can resolve a rate. |
| `AdminReports:CsvRowLimit` | `50000` | Streaming CSV row cap. |
| `Admin:DefaultPassword` | (configured) | Sentinel admin password outside ephemeral. |
| `Storage:Provider` | `Azurite` (dev) / `AzureBlob` (prod) / `LocalFilesystem` (fallback) | Spec 014 — selects the `IObjectStorage` impl. Fail-fast in `Production` if `LocalFilesystem` is paired with a connection string. |
| `Storage:LocalFilesystem:RootPath` | `./.localstorage` | Required when `Provider=LocalFilesystem`. Host is responsible for encryption-at-rest (FR-026 — local provider does **not** provide it). |
| `Storage:Categories:{name}:MaxSizeBytes` | per-category default | Per-`FileCategory` upload cap enforced at the controller boundary by `UploadSizeGuard` (spec 014 / FR-021). |
| `Storage:Categories:{name}:UrlExpirySeconds` | `300` (5 min, max 900) | SAS URL TTL when `ServingMode=TimeLimitedUrl`. Hard cap is 15 min (FR-019). |
| `Storage:Categories:{name}:RetentionPolicy` | `none` | Future-seam string. `signed-funding-agreements` is the legal-hold candidate (FR-023). |
| `Storage:TestFallback:AllowFilesystem` | `false` | When `true`, the E2E `AspireFixture` may swap to `LocalFilesystem` if Azurite cannot start (FR-008). Logs a warning. |
| `Notifications:Provider` | `Mailtrap` (Local) / `Mailgun` (non-Local) | Spec 021 — selects the `IEmailSender` impl. Absence of provider config in non-Production → `NoOpEmailSender` with WARN log (FR-015). |
| `Notifications:BaseUrl` | per env | Absolute base URL used to compose CTA deep links in email bodies (FR-026). |
| `Notifications:NonProdAllowlist` | `["@programa-semilla.test"]` (dev) | Spec 021 / FR-017 — recipients whose email or `@domain` is not in the list are dropped and recorded as `BlockedByAllowlist`. Empty list is fail-closed. Bypassed in Production (FR-019). |
| `Notifications:Mailgun:ApiKey` / `Domain` / `BaseUrl` | `` / `` / `https://api.mailgun.net/v3` | Mailgun HTTP API config (FR-014). AppHost fails fast in Production when any of ApiKey/Domain/Sender:Email/BaseUrl is missing (FR-016). |
| `Notifications:Mailtrap:Host` / `Port` / `Username` / `Password` | from Aspire smtp4dev binding | SMTP path config; in Local resolved automatically from the Aspire smtp4dev sidecar endpoint. |
| `Notifications:Worker:PollIntervalSeconds` / `MaxAttempts` / `BatchSize` | `5` / `3` / `25` | `EmailDispatchWorker` poll cadence + retry budget + per-poll claim batch size (FR-003, FR-021). |
| `Notifications:Sender:Name` / `Email` | `Programa Semilla / Sistema de Banca para el Desarrollo` / `no-reply@programa-semilla.cr` | RFC-5322 From: display + address used by every variant (FR-014, spec 019 sender display). |

### Spec 021 sidecar

The Aspire AppHost registers `rnwood/smtp4dev` as a container resource named `smtp4dev` with two endpoints (`smtp` TCP 25, `http` 80). The Web project consumes both via `WithReference` so `MailtrapSmtpEmailSender` resolves the dynamic SMTP host:port; the E2E `MailCaptureClient` consumes the HTTP REST API.

## Testing

- Unit: `dotnet test tests/FundingPlatform.Tests.Unit`
- Integration: `dotnet test tests/FundingPlatform.Tests.Integration`
- E2E: `dotnet test tests/FundingPlatform.Tests.E2E`
- E2E uses `AspireFixture`, which boots AppHost with `--EphemeralStorage=true`. Each fixture run starts with a clean SQL Server container; the fixture deploys the dacpac via `sqlpackage` and waits synchronously before tests start.
- Sentinel admin in ephemeral E2E: `admin@programa-semilla.test` / `Sentinel123!`. Demo seeds: `applicant@programa-semilla.test`, `reviewer@programa-semilla.test`, `demo-admin@programa-semilla.test`, `supplieradmin@programa-semilla.test` (all `Demo123!`). All seed emails live in the `Notifications:NonProdAllowlist` default `["@programa-semilla.test"]` so mail captures in smtp4dev without further config.
- Integration tests must hit a real DB, never mocks. Mocks burned the team on a prod migration last quarter.
- Delivery bar: a feature is not delivered until the **full E2E suite has been personally executed and is green**. Structural readiness, type-checks, or partial runs do not count.

## Conventions

- Default culture: **es-CR**. Translation/localization is in scope (spec 012). UI copy should not be English-only.
- UI: Tabler.io vendored under `wwwroot/lib/`. Fonts (Fraunces / Inter / JetBrains Mono) and `canvas-confetti` also vendored. **No CDN — all static assets are local.**
- New managed (NuGet) dependencies require spec approval. Default posture: reuse what is vendored.
- PDF generation: Syncfusion needs a license key at runtime (`SyncfusionLicenseValidator`). The dev fallback in `AppHost.cs` lets local runs work; real envs must override.
- Speckit checkpoints: at every phase checkpoint, commit and push without prompting.
- UX/UI quality wins over E2E selector stability. HTML restructuring + E2E rewrites are in scope when elevating UI.

## Deploy to Azure

The `dev` environment lives in Azure subscription **LinaSys-DevEnv** (`d428f98f-…`), resource group `rg-CapitalSemilla-D`, region centralus, azd env name `CapitalSemilla-D`. Container App `webapp`. Custom domain: `https://capitalsemilla-dev.programasemilla.com/`.

### Deploy steps (in order)

1. **Code + container env:** `cd src/FundingPlatform.AppHost && azd up` (incremental — infra already provisioned). For a code-only redeploy: `azd deploy webapp`.
2. **Schema (NOT auto-deployed in publish mode):** the AppHost only registers the SqlProject in run mode, so Azure SQL is never updated by `azd up`. After every code deploy that changes schema, run `bash scripts/publish-dacpac-azure.sh`. The script handles dacpac build + transient firewall + AAD token. Defaults are destructive (`BlockOnPossibleDataLoss=false`, `DropObjectsNotInSource=true`). **Always use `--no-drop` for this env** — otherwise sqlpackage drops the contained DB user `mi-negelwcexrtzc` (created by the `sqlserver-roles` bicep outside the dacpac) and the running app loses DB auth.
3. **Auth to deploy schema:** your AAD account needs DDL on `fundingdb`. Server AAD admin is normally `sqlserver-admin-negelwcexrtzc` (sid `e991a9d7-28bc-46d5-ba15-bf68559c8541`). If your `az` user isn't that admin, temporarily set yourself: `az sql server ad-admin update -g rg-CapitalSemilla-D -s sqlserver-negelwcexrtzc --display-name "<you>" --object-id "<your-objectId>"`, deploy, then restore with `--display-name sqlserver-admin-negelwcexrtzc --object-id e991a9d7-28bc-46d5-ba15-bf68559c8541`.
4. **Tag the deployed commit on `main`:**
   ```bash
   git tag -a deploy/<env>/<YYYY-MM-DD>[-<N>] -m "<env> environment release to Azure (...)"
   git push origin deploy/<env>/<YYYY-MM-DD>[-<N>]
   ```
   Convention: `deploy/<env>/<YYYY-MM-DD>` where `<env>` is `dev|staging|prod`. Suffix `-2`, `-3`, … when more than one deploy lands the same day. Tags are **annotated** and point at the merged-to-main commit that was built and shipped. List releases: `git tag -l 'deploy/dev/*'`.

### Things that bite during a fresh deploy

- `azd` does **not** forward azd-environment (`.env`) values into the AppHost's manifest-generation subprocess. Code that reads runtime business config via `builder.Configuration[...]` inside `if (IsPublishMode)` will see `null` and must not throw on absent values — runtime fail-fasts belong in the Web project, not the AppHost.
- The on-disk Container App template `infra/webapp.tmpl.yaml` is the source of truth for the deployed container env (and overrides the AppHost's `WithEnvironment` literals once `infra/` exists on disk). Per `next-steps.md`: drift here will silently outlast AppHost changes. Reference azd-env via `{{ .Env.X }}` for non-secrets and Container App secrets (`secretRef:`) for credentials, both sourced from azd-env.
- `azd deploy` has occasionally shipped a BuildKit-cache-stale image. If a fresh deploy ships missing files that are clearly in the Dockerfile, `docker builder prune` then redeploy.

### Alternative: single-VM fixed-cost deploy (`deploy/vm/`)

A second, **fixed-monthly-cost** deployment path that does not use azd/Container Apps. Azure has no native "stop at $X" (budgets only alert; Container Apps + Azure SQL bill by usage), so a single VM's fixed compute cost is the lever. One Linux VM runs everything via Docker Compose: Caddy (auto Let's Encrypt TLS for `capitalsemilla-dev.programasemilla.com`) + the webapp (built on the VM from `src/FundingPlatform.Web/Dockerfile`) + SQL Server 2022 container; attachments go to Azure Blob via the VM's managed identity (cheap, durable), with LocalFilesystem as a fallback. Logs stay on the VM (`docker compose logs`, or the optional in-memory Aspire Dashboard) — **zero Log Analytics cost**. Full runbook: `deploy/vm/README.md`.

One-time provision (from dev machine, needs `az`): `deploy/vm/provision-vm.sh` (creates the RG + VM + NSG) → `deploy/vm/provision-storage.sh` (Blob account + managed-identity grant) → point DNS at the printed IP → create `deploy/vm/.env` on the VM from `.env.example`.

Deploy / update (idempotent, from dev machine) — `deploy/vm/deploy.sh` rsyncs the repo to the VM (never touching the VM's `.env`), rebuilds the image on the VM, recreates only what changed:

```bash
./deploy.sh <VM IP>                                  # push a code update
MSSQL_SA_PASSWORD='…' ./deploy.sh <VM IP> --schema   # also publish the dacpac
./deploy.sh <VM IP> --no-build                       # recreate without rebuilding
./deploy.sh <VM IP> --logs                           # tail webapp+caddy after
```

Schema-only publish (also used by `--schema`): `deploy/vm/publish-dacpac-vm.sh <VM IP>` (dacpac over an SSH tunnel; SQL is loopback-only on the VM). Note: the `dev` Azure resource group was deleted on 2026-06-01 to stop a Log Analytics billing spike, so this VM path is the current forward plan rather than the azd stack.

## Specs

`specs/NNN-slug/` is the source of truth for feature intent — spec.md, plan.md, tasks.md, and contracts. Read the spec before changing behavior in that area. Specs span 001-core-model-submission through 028-post-resolution-notifications (note: two `021-` slugs exist — `021-email-notifications` and `021-feedback-session-may13`).

<!-- MANUAL ADDITIONS START -->

<!-- MANUAL ADDITIONS END -->

## Managed dependencies (each added one required spec approval per Conventions)

- `Anthropic.SDK` NuGet — AI quote comparison (`IAiClient`), approved via spec 020 (A-10).
- MailKit v3 (MIT) — SMTP `IEmailSender` path, spec 021. Note: spec 021-feedback-session-may13's `SmtpEmailSender` deliberately uses `System.Net.Mail.SmtpClient` instead (no MailKit, per its NFR-005).
- smtp4dev (`rnwood/smtp4dev`) — Aspire container resource in Local only; Mailgun HTTP API is the non-Local provider (spec 021).
- Azurite — Docker container for blob storage in dev+test; Azure Blob in prod, local-filesystem fallback (spec 014).

Per-spec architectural seams (interfaces, aggregates, tables) are summarized in **Recent Changes** below and detailed in each `specs/NNN-slug/plan.md`.

## Recent Changes
- 028-post-resolution-notifications: 12 new post-`Resolved` `NotificationEvent` values (applicant-response → reviewer, appeal lifecycle, full signing ceremony) wired through the existing spec-021 outbox → `EmailDispatchWorker` → `IEmailSender` → allowlist pipeline; cross-cutting event-aware CTA (`CtaRouteTemplate` on `Binding`) + actor-exclusion (`NotificationPayload.ActorUserId`); 24 es-CR Razor partials under `Views/Emails/`; no schema change. Shipped to main via PR #38 (squash `5b965fb`). STAMP PASS (E2E 291/0/5)
- 027-review-funding-ux: Eight reviewer/applicant funding-agreement UX refinements making submitted-item decision data legible + consistent at every touchpoint; es-CR; PDF body explicitly unchanged (spec 018 minimalism preserved); no schema change
- 026-input-masks: Extensible structured-field input masks for email, CR phone, and CR identification numbers (cédula física/jurídica, DIMEX, NITE, passport) — type-aware ID entry + submit-time rejection of malformed values; completes spec 021 FR-013
- 025-supplier-location-cascade: Wires the never-rendered Provincia → Cantón cascade from spec 021 FR-014 and adds the Distrito level — full three-level Costa Rica hierarchy on the supplier-branch form
- 024-toast-confirm-dialogs: Unified in-app messaging — replaces TempData banner alerts + native `window.alert`/`confirm()` with one consistent toast + modal-dialog system across all pages and roles
- 023-quotation-edit: In-place per-quotation Edit affordance for the Application owner while `Draft` — editable Price / Currency / ValidUntil / SupplierBranchId (same supplier); persistence through existing `Quotation.EditAmount` / `ChangeCurrencyAsync` + new `Quotation.ChangeBranch` invariant; `_QuoteFields.cshtml` extracted from Supplier/Add; ModelState-aggregated server validation; `ComparisonArtifact` cache invalidation on save; 3 per-US Playwright E2E classes; no schema change. STAMP PASS (FR 11/11, SC 8/8). Variance: spec FR-008 names `ReturnedForChanges` but codebase has no such enum — `SendBack` returns to `Draft`, gate is on `Draft` (REVIEW-CODE Deviation #1, evolve post-merge)
- 022-combined-release: 020 + 021 merged for joint PR; no behavioral changes beyond per-spec contracts; conflict-resolution log in `specs/022-combined-release/plan.md`
- 021-feedback-session-may13: Consolidated implementation of the 26 May-13 stakeholder refinements across US1–US8 — Process/Plantilla/PublicCode/Impact-at-Application, supplier autocomplete + Province/Cantón cascade + new-supplier inline branch, autosave-on-blur + masks + required markers + submit gating + `/review` confirmation, profile + forgot-password flows, stage-expiry windows + reminder emails, acompañamiento copy pivot + landing scaffold, admin KPI repivot + deleted-still-active bug fix
- 021-email-notifications: First email-notification subsystem — transactional outbox + BackgroundService dispatcher, six v1 events (APPLICATION_SUBMITTED_REVIEWER/APPLICANT, RETURNED_TO_APPLICANT, RESUBMITTED_BY_APPLICANT, APPLICATION_APPROVED/REJECTED), es-CR Razor templates with text-only wordmark, fail-closed allowlist guard outside Production, idempotency via `(EventType, ApplicationId, VersionHistoryId, RecipientUserId)` unique index, smtp4dev capture sidecar wired into Aspire, `EmailTemplateSenderTests.Assert.Ignore` replaced with real captures
- 020-ai-quote-comparison: AI-powered per-item supplier-quotation comparison persisted as hash-keyed `ComparisonArtifact`, three-stage `extract → normalize → compare` pipeline behind `IComparisonOrchestrator`, single Anthropic provider behind `IAiClient`, PII redaction at the boundary, per-app rate limit + per-run token cap with admin bypass, hosted-service worker + 3 s polling for "Generar todo", numeric-superscript citations linking back to supplier blobs via existing storage signed URLs, es-CR output, reused `AdminAuditEvent` (FR-H1, SC-001..012)
- 017-admin-ux-facelift: `/Admin` becomes a capability-complete dashboard (4 action KPIs + 9 grouped capability cards + optional activity feed); 10-surface admin sweep at spec 011 quality bar; sidebar admin grouping; route normalization (AdminCurrencies/AdminExchangeRates/AdminLegacyQuotations); Reports tab UX refresh; schema unchanged (FR-027 / SC-016)
- 016-user-groups: Group-scoped reviewer access — `Group` + `UserGroupMembership` + `AdminAuditEvent`, admin Groups CRUD, multi-select group selector on the user form, EF-level group-overlap predicate on queue / signing inbox / detail-page auth, FR-014 reviewer queue search input
- 015-multi-currency-quotes: Multi-currency supplier quotations (CRC base + USD), buy-rate snapshotting, agreement PDF conversion notes

<!-- SPECKIT START -->
No active plan. (Spec 028 post-resolution-notifications shipped to main via PR #38, squash commit `5b965fb`. The leftover `028-post-resolution-notifications` branch is the already-merged source branch — stale.)
<!-- SPECKIT END -->
