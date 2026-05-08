# Capital Semilla / FundingPlatform

Last updated: 2026-05-01

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
| `FundingAgreement:Funder:*` | empty | Legal name, tax id, address, contact email/phone. |
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

## Testing

- Unit: `dotnet test tests/FundingPlatform.Tests.Unit`
- Integration: `dotnet test tests/FundingPlatform.Tests.Integration`
- E2E: `dotnet test tests/FundingPlatform.Tests.E2E`
- E2E uses `AspireFixture`, which boots AppHost with `--EphemeralStorage=true`. Each fixture run starts with a clean SQL Server container; the fixture deploys the dacpac via `sqlpackage` and waits synchronously before tests start.
- Sentinel admin in ephemeral E2E: `admin@FundingPlatform.com` / `Sentinel123!`.
- Integration tests must hit a real DB, never mocks. Mocks burned the team on a prod migration last quarter.
- Delivery bar: a feature is not delivered until the **full E2E suite has been personally executed and is green**. Structural readiness, type-checks, or partial runs do not count.

## Conventions

- Default culture: **es-CR**. Translation/localization is in scope (spec 012). UI copy should not be English-only.
- UI: Tabler.io vendored under `wwwroot/lib/`. Fonts (Fraunces / Inter / JetBrains Mono) and `canvas-confetti` also vendored. **No CDN — all static assets are local.**
- New managed (NuGet) dependencies require spec approval. Default posture: reuse what is vendored.
- PDF generation: Syncfusion needs a license key at runtime (`SyncfusionLicenseValidator`). The dev fallback in `AppHost.cs` lets local runs work; real envs must override.
- Speckit checkpoints: at every phase checkpoint, commit and push without prompting.
- UX/UI quality wins over E2E selector stability. HTML restructuring + E2E rewrites are in scope when elevating UI.

## Specs

`specs/NNN-slug/` is the source of truth for feature intent — spec.md, plan.md, tasks.md, and contracts. Read the spec before changing behavior in that area. Active specs span 001-core-model-submission through 014-azure-blob-storage.

<!-- MANUAL ADDITIONS START -->

<!-- MANUAL ADDITIONS END -->

## Active Technologies
- C# 13 / .NET 10.0 (014-azure-blob-storage)
- C# 13 / .NET 10.0 (015-multi-currency-quotes — Currencies, ExchangeRates, snapshot-locked Quotation conversion)
- Azure Blob Storage in production / Azurite (Docker container) in dev+test / local filesystem fallback. SQL Server unchanged. (014-azure-blob-storage)

## Recent Changes
- 015-multi-currency-quotes: Multi-currency supplier quotations (CRC base + USD), buy-rate snapshotting, agreement PDF conversion notes
- 014-azure-blob-storage: Added C# 13 / .NET 10.0

<!-- SPECKIT START -->
For additional context about technologies to be used, project structure,
shell commands, and other important information, read the current plan:
`specs/016-user-groups/plan.md`
<!-- SPECKIT END -->
