# bsd-ps Development Guidelines

Auto-generated from all feature plans. Last updated: 2026-04-30

## Active Technologies
- C# / .NET 10.0 + ASP.NET MVC, Entity Framework Core 10.0, ASP.NET Identity, .NET Aspire (002-review-approval-workflow)
- SQL Server (Aspire-managed container for dev, dacpac schema management) (002-review-approval-workflow)
- C# / .NET 10.0 + ASP.NET MVC, Entity Framework Core 10.0, ASP.NET Identity, .NET Aspire, Syncfusion.HtmlToPdfConverter.Net.Linux (31.2.x), Syncfusion.Licensing (005-funding-agreement-generation)
- SQL Server (Aspire-managed container for dev, dacpac schema management); local file system for PDF bytes via `IFileStorageService` (005-funding-agreement-generation)
- C# / .NET 10.0 + ASP.NET MVC, Entity Framework Core 10.0, ASP.NET Identity, .NET Aspire. No new dependencies introduced by this feature. (006-digital-signatures)
- SQL Server (Aspire-managed container for dev, dacpac schema management); local file system for signed-PDF bytes via the existing `IFileStorageService`. No new storage subsystems. (006-digital-signatures)
- SQL Server (Aspire-managed container for dev, dacpac schema management). **No schema changes.** No new storage subsystems. (007-signing-wayfinding)
- C# / .NET 10.0 (matches all prior specs) + ASP.NET MVC, Entity Framework Core 10.0, ASP.NET Identity, .NET Aspire. **NEW vendored static-asset dependency**: Tabler.io open-source build (CSS + JS) and Tabler Icons. No new NuGet packages, no new managed dependencies. (008-tabler-ui-migration)
- SQL Server (Aspire-managed for dev, dacpac schema management). **No schema changes.** (008-tabler-ui-migration)
- C# / .NET 10.0 (matches all prior specs). + ASP.NET MVC, Entity Framework Core 10.0, ASP.NET Identity, .NET Aspire. **No new dependencies introduced by this feature.** The Tabler.io static-asset bundle vendored by spec 008 is reused as-is. (009-admin-area)
- SQL Server (Aspire-managed for dev, dacpac schema management). **Schema change**: four new columns on `dbo.AspNetUsers` (`FirstName`, `LastName`, `IsSystemSentinel`, `MustChangePassword`) plus one filtered index on `IsSystemSentinel = 1` for fast sentinel lookup. No new tables. No new managed storage subsystems. (009-admin-area)
- C# / .NET 10.0 (matches all prior specs). + ASP.NET MVC, Entity Framework Core 10.0, ASP.NET Identity, .NET Aspire. **No new dependencies introduced by this feature.** The Tabler.io static-asset bundle vendored by spec 008 is reused as-is. The Syncfusion HTML-to-PDF renderer and license validator vendored by spec 005 are reused as-is for the Funding Agreement currency-code render change. (010-admin-reports)
- SQL Server (Aspire-managed for dev, dacpac schema management). **Schema change**: one new column on `dbo.Quotations` (`Currency` NVARCHAR(3) NOT NULL after backfill). One new seed row in `dbo.SystemConfigurations` (`DefaultCurrency`). No new tables. No new managed storage subsystems. CSV exports stream directly from the DB to the HTTP response — no temp file, no in-memory materialization beyond the page-buffer. (010-admin-reports)
- C# / .NET 10.0 (matches all prior specs). + ASP.NET MVC, Entity Framework Core 10.0, ASP.NET Identity, .NET Aspire. **No new managed dependencies.** New **static-asset** vendored dependencies only: Fraunces (display serif, SIL OFL), Inter (body sans, SIL OFL), JetBrains Mono (monospace, Apache 2.0), 9 in-house empty-state SVG illustrations, and `canvas-confetti` (≤ 5 KB gz, vendored as a static `.js`) — all served from `wwwroot/lib/`. Tabler.io static-asset bundle (vendored by spec 008) and Syncfusion HTML-to-PDF (vendored by spec 005) remain unchanged. (011-warm-modern-facelift)
- SQL Server (Aspire-managed for dev, dacpac schema management). **No schema changes** (FR-067). Wow-moment data flows through new Application-layer query/projection services (e.g., `IApplicantDashboardProjection`, `IReviewerQueueProjection`, `IJourneyProjector`) that read existing aggregates. (011-warm-modern-facelift)
- C# / .NET 10.0 (matches all prior specs). + ASP.NET MVC, Entity Framework Core 10.0, ASP.NET Identity, .NET Aspire, Syncfusion HTML-to-PDF (existing — vendored by spec 005), Tabler.io static-asset bundle (existing — vendored by spec 008), Fraunces / Inter / JetBrains Mono / canvas-confetti static assets (existing — vendored by spec 011). **Zero new managed dependencies.** **Zero new vendored static assets** (other than the new "Capital Semilla" wordmark SVG, which is a designer artifact, not a library). (012-es-cr-localization)
- SQL Server (Aspire-managed for dev, dacpac schema management). **No schema changes.** Local file system for PDFs (existing). **No new storage subsystems.** (012-es-cr-localization)
- C# / .NET 10.0 (matches all prior specs). + ASP.NET MVC, Entity Framework Core 10.0 (data access only), ASP.NET Identity, .NET Aspire. No new managed dependencies. Reuses existing static-asset stacks: Tabler.io (spec 008), Fraunces / Inter / JetBrains Mono / canvas-confetti (spec 011), Syncfusion HTML-to-PDF (spec 005, untouched by this feature). (013-supplier-catalog)
- SQL Server (Aspire-managed for dev, dacpac schema management). **Schema change**: new `dbo.SupplierBranches` table (1:N under `dbo.Suppliers`); five new columns on `dbo.Suppliers` (`VerificationStatus`, `CreatedByApplicantId`, `VerifiedByUserId`, `VerifiedAt`, `RejectionReason`); six existing columns dropped from `dbo.Suppliers` (`ContactName`, `Email`, `Phone`, `Location`, `ShippingDetails`, `WarrantyInfo`) after migration; one new column on `dbo.Quotations` (`SupplierBranchId`). One filtered unique index on `SupplierBranches (SupplierId, IsDefault)` enforces single default per supplier. Local file system for documents (existing). No new storage subsystems. (013-supplier-catalog)

- C# / .NET 8+ (latest LTS) + ASP.NET MVC, .NET Aspire, EF Core, ASP.NET Identity, Playwright (001-core-model-submission)

## Project Structure

```text
src/
tests/
```

## Commands

# Add commands for C# / .NET 8+ (latest LTS)

## Code Style

C# / .NET 8+ (latest LTS): Follow standard conventions

## Recent Changes
- 013-supplier-catalog: Added C# / .NET 10.0 (matches all prior specs). + ASP.NET MVC, Entity Framework Core 10.0 (data access only), ASP.NET Identity, .NET Aspire. No new managed dependencies. Reuses existing static-asset stacks: Tabler.io (spec 008), Fraunces / Inter / JetBrains Mono / canvas-confetti (spec 011), Syncfusion HTML-to-PDF (spec 005, untouched by this feature).
- 012-es-cr-localization: Added C# / .NET 10.0 (matches all prior specs). + ASP.NET MVC, Entity Framework Core 10.0, ASP.NET Identity, .NET Aspire, Syncfusion HTML-to-PDF (existing — vendored by spec 005), Tabler.io static-asset bundle (existing — vendored by spec 008), Fraunces / Inter / JetBrains Mono / canvas-confetti static assets (existing — vendored by spec 011). **Zero new managed dependencies.** **Zero new vendored static assets** (other than the new "Capital Semilla" wordmark SVG, which is a designer artifact, not a library).
- 011-warm-modern-facelift: Added C# / .NET 10.0 (matches all prior specs). + ASP.NET MVC, Entity Framework Core 10.0, ASP.NET Identity, .NET Aspire. **No new managed dependencies.** New **static-asset** vendored dependencies only: Fraunces (display serif, SIL OFL), Inter (body sans, SIL OFL), JetBrains Mono (monospace, Apache 2.0), 9 in-house empty-state SVG illustrations, and `canvas-confetti` (≤ 5 KB gz, vendored as a static `.js`) — all served from `wwwroot/lib/`. Tabler.io static-asset bundle (vendored by spec 008) and Syncfusion HTML-to-PDF (vendored by spec 005) remain unchanged.


<!-- MANUAL ADDITIONS START -->

## Testing conventions

- **E2E tests use ephemeral SQL.** `AppHost.cs` skips `WithDataVolume("fundingplatform-sqldata")` when `--EphemeralStorage=true` is passed, and `AspireFixture` passes that flag — so every E2E fixture run starts with a clean SQL Server container. `dotnet run --project src/FundingPlatform.AppHost` (dev mode) keeps the persistent volume.

<!-- MANUAL ADDITIONS END -->
