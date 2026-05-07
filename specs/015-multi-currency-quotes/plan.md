# Implementation Plan: Suppliers Quotes Multi-Currency

**Branch**: `015-multi-currency-quotes` | **Date**: 2026-05-06 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/015-multi-currency-quotes/spec.md`

## Summary

Enable supplier quotes to be entered in either CRC (base) or USD, with administrator-managed currency enablement and CRC↔USD reference exchange rates (buy/sell, immutable once used). Each non-CRC quote snapshots the applied rate at save time so historical converted CRC values remain stable across rate changes. All UI surfaces, totals, and admin reports show original + converted CRC with a conversion indicator. The final agreement PDF renders CRC only with a per-line conversion note (rate + effective date) when any line originated in USD. Decimal-only arithmetic, half-away-from-zero rounding on the converted CRC line, totals summed from rounded line values. Migration auto-stamps CRC quotes and quarantines legacy non-CRC quotes with a "needs review" flag until an admin attaches a historical rate.

## Technical Context

**Language/Version**: C# 13 / .NET 10.0
**Primary Dependencies**: ASP.NET MVC, EF Core 10, ASP.NET Identity, .NET Aspire, Syncfusion HtmlToPdfConverter (PDF), Tabler.io vendored CSS/JS (UI)
**Storage**: SQL Server (Aspire-managed container in dev, Azure SQL in prod via spec 017). Schema source-of-truth is the `FundingPlatform.Database` dacpac. **No EF migrations.**
**Testing**: xUnit (unit + integration), Playwright for .NET (E2E via `AspireFixture` with ephemeral SQL container)
**Target Platform**: Linux server (Aspire orchestrator), browser UI rendered server-side
**Project Type**: ASP.NET MVC web application with Clean-Architecture layering (`Domain` / `Application` / `Infrastructure` / `Web`) plus dacpac and Aspire AppHost
**Performance Goals**: Conversion preview returns within 200 ms p95 (single round-trip to the latest-rate cache); admin rate save < 500 ms; quote save with conversion < 800 ms; CSV export of 10k mixed-currency lines streams without buffering the full result.
**Constraints**: Decimal-only arithmetic (no `double`/`float` in the conversion path); CRC base currency permanently enabled; immutable-once-used rate semantics enforced server-side; PDF must be value-stable across regenerations.
**Scale/Scope**: Two currencies (CRC, USD); rate-record growth ~ a few hundred per year (manual admin entry, ~daily); supplier-quote volume governed by existing platform load (low-thousands of quotes per active period).

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Notes |
|---|---|---|
| I. Clean Architecture | PASS | New entities (`Currency`, `ExchangeRate`) live in `Domain`; conversion is an `IConversionService` in `Application` (interface) implemented in `Infrastructure`; controllers in `Web`. Dependencies inward. No leaks across boundaries. |
| II. Rich Domain Model | PASS | `ExchangeRate` exposes behavior (`ConvertUsdToCrc`, `MarkUsed`, validation invariants on construction). `SupplierQuote` gains `SetCurrency`, `ApplyRateSnapshot`, `EditAmount` methods (snapshot reapplied) — no anemic public setters. |
| III. End-to-End Testing (Non-Negotiable) | PASS | Each user story has its own E2E test class. Page-Object Model for the admin currency page, admin rate page, quote form, and the PDF download/inspection flow. Tests run under `AspireFixture`. |
| IV. Schema-First DB Management | PASS | All schema additions (Currency, ExchangeRate, SupplierQuote columns, audit-log additions, seed data for CRC + USD + initial rate) are edited in `FundingPlatform.Database` `.sql` files. EF Core configures mappings only. Post-deploy script seeds CRC (always-enabled, base) and USD (enabled). |
| V. Specification-Driven Development | PASS | `spec.md` complete, this `plan.md` covers technical approach, `tasks.md` (Phase 2) will follow story priorities P1→P3. |
| VI. Simplicity / Progressive Complexity | PASS | Hard-coded MVP (2 currencies, no scheduling, no approval, no per-quote override, no stale-rate notifications). Future-extensibility deferred to a later spec. Complexity Tracking table is empty — no justified violations. |

**Verdict**: PASS. No constitution violations.

## Project Structure

### Documentation (this feature)

```text
specs/015-multi-currency-quotes/
├── plan.md              # This file
├── spec.md              # Feature specification
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output
│   ├── currency-api.md            # Admin currency listing + enable/disable
│   ├── exchange-rate-api.md       # Admin rate CRUD (create-only, read history)
│   └── conversion-preview-api.md  # Quote-form conversion-preview endpoint
└── tasks.md             # Phase 2 output (created by /speckit-tasks)
```

### Source Code (repository root)

This feature is additive to the existing FundingPlatform layout. Directories that get new files are listed; existing directories not listed are unchanged.

```text
src/
├── FundingPlatform.Domain/
│   ├── Currencies/
│   │   ├── Currency.cs                       # Value object / entity (ISO code, symbol, name, precision, enabled, base, displayOrder)
│   │   └── CurrencyCode.cs                   # Strongly-typed wrapper for ISO codes
│   ├── ExchangeRates/
│   │   ├── ExchangeRate.cs                   # Aggregate (immutable-once-used semantics)
│   │   ├── ExchangeRateSnapshot.cs           # Value object embedded into SupplierQuote
│   │   └── RateType.cs                       # enum { Buy, Sell }
│   └── SupplierQuotes/
│       └── SupplierQuote.cs                  # Extended with CurrencyCode + Snapshot fields + LegacyNeedsReview flag
│
├── FundingPlatform.Application/
│   ├── Currencies/
│   │   ├── ICurrencyConfigService.cs
│   │   └── CurrencyConfigService.cs          # Enable/disable USD; CRC always-enabled invariant
│   ├── ExchangeRates/
│   │   ├── IExchangeRateService.cs
│   │   ├── ExchangeRateService.cs            # Create-only; reject zero/negative/duplicate-timestamp/future-dated; mark previous rate superseded conceptually (no edit)
│   │   └── IConversionService.cs             # ConvertUsdToCrc(amount, asOf?) — pulls latest applicable rate, returns (crcAmount, rateSnapshot)
│   └── SupplierQuotes/
│       ├── ApplySnapshotOnSave.cs            # Use case helper — server-side conversion at save
│       └── LegacyQuoteRateAttachService.cs   # Admin attaches a historical rate to a flagged legacy quote
│
├── FundingPlatform.Infrastructure/
│   ├── Persistence/
│   │   ├── Configurations/
│   │   │   ├── CurrencyConfiguration.cs
│   │   │   ├── ExchangeRateConfiguration.cs
│   │   │   └── SupplierQuoteConfiguration.cs # extended
│   │   └── ApplicationDbContext.cs            # adds DbSet<Currency>, DbSet<ExchangeRate>; SupplierQuote already mapped
│   ├── Conversion/
│   │   └── ConversionService.cs              # Reads latest rate; applies decimal arithmetic + half-away-from-zero rounding
│   └── Pdf/
│       └── AgreementPdfRenderer.cs           # Extended: emit conversion note rows when any line is non-CRC; refuse on missing snapshot
│
├── FundingPlatform.Web/
│   ├── Areas/Admin/Controllers/
│   │   ├── CurrenciesController.cs
│   │   └── ExchangeRatesController.cs
│   ├── Areas/Admin/Views/
│   │   ├── Currencies/Index.cshtml
│   │   └── ExchangeRates/{Index,Create,History}.cshtml
│   ├── Controllers/
│   │   ├── SupplierQuotesController.cs       # extend create/edit; new Convert action for preview JSON
│   │   └── AgreementsController.cs           # extend Pdf action to surface FR-027 inline error
│   ├── ViewComponents/
│   │   ├── MoneyDisplayViewComponent.cs      # Shared formatter: original + CRC + conversion-indicator tooltip
│   │   └── ConversionIndicatorViewComponent.cs
│   └── wwwroot/js/
│       └── quote-conversion-preview.js       # Calls Convert action on currency/amount change
│
├── FundingPlatform.Database/                 # dacpac — single source of truth
│   ├── Tables/
│   │   ├── Currencies.sql                    # NEW
│   │   ├── ExchangeRates.sql                 # NEW
│   │   └── SupplierQuotes.sql                # ALTERED: +OriginalCurrencyCode, +OriginalAmount, +ConvertedCrcAmount, +SnapshotRateValue, +SnapshotRateType, +SnapshotEffectiveAtUtc, +SnapshotRateId, +LegacyNeedsReview
│   ├── Indexes/
│   │   ├── IX_ExchangeRates_PairEffectiveAt.sql
│   │   └── IX_SupplierQuotes_LegacyNeedsReview.sql
│   └── PostDeploy/
│       └── 015-currency-seed.sql             # Seed CRC (base, enabled, non-disable-able), USD (enabled)
│
└── (no changes outside above)

tests/
├── FundingPlatform.Tests.Unit/
│   ├── ExchangeRateTests.cs                  # invariants, immutable-once-used, validation
│   ├── ConversionServiceTests.cs             # decimal arithmetic, rounding edge cases
│   └── SupplierQuoteTests.cs                 # SetCurrency, EditAmount snapshot reapply
├── FundingPlatform.Tests.Integration/
│   ├── ExchangeRateRepositoryTests.cs
│   ├── CurrencyConfigServiceTests.cs
│   ├── MigrationTests.cs                     # post-deploy stamps CRC, flags legacy non-CRC
│   └── PdfRenderingTests.cs                  # CRC-only request, mixed request, missing-snapshot refusal
└── FundingPlatform.Tests.E2E/
    ├── AdminCurrencyConfigE2E.cs             # User Story 3 (admin)
    ├── AdminExchangeRateE2E.cs               # User Story 3 (admin)
    ├── ApplicantUsdQuoteE2E.cs               # User Story 1
    ├── ApplicantCrcQuoteE2E.cs               # User Story 2
    ├── ReviewerDisplayE2E.cs                 # User Story 4
    ├── AgreementPdfMultiCurrencyE2E.cs       # User Story 5
    └── LegacyQuoteFlowE2E.cs                 # User Story 6
```

**Structure Decision**: Single Clean-Architecture solution under `src/` with the `Domain → Application → Infrastructure → Web` layering already established by FundingPlatform. The dacpac (`FundingPlatform.Database`) owns schema. Tests are split by category under `tests/` and run from Aspire AppHost via `AspireFixture` for integration + E2E.

## Phase 0 — Research

Research outputs in [research.md](./research.md) cover:

- Decimal arithmetic conventions in C# (`decimal` type, `MidpointRounding.AwayFromZero`).
- Strategy for "immutable-once-used" enforcement at the persistence layer.
- Rate-snapshot embedding vs. pointer-only — chose **embed + reference** for value stability and audit traceability.
- PDF rendering pattern for conversion notes within Syncfusion HtmlToPdfConverter (Linux, Chromium runtime).
- Migration approach using a post-deploy SQL script idempotent on re-deploy.
- Concurrency posture: optimistic concurrency on `ExchangeRate` insert (unique `(SourceCurrency, TargetCurrency, EffectiveAtUtc)` index).
- Latest-rate read pattern: indexed query (no caching layer in MVP).

All NEEDS-CLARIFICATION items from the spec were resolved in clarify (Q1–Q4). No remaining unknowns.

## Phase 1 — Design & Contracts

### Data model

See [data-model.md](./data-model.md). Two new tables (`Currencies`, `ExchangeRates`) plus column extensions on `SupplierQuotes`. Detailed field types, indexes, and post-deploy seed are documented there.

### Contracts

Three contract documents are produced under `contracts/`:

- [`currency-api.md`](./contracts/currency-api.md) — Admin currency listing + enable/disable.
- [`exchange-rate-api.md`](./contracts/exchange-rate-api.md) — Admin rate create + history list.
- [`conversion-preview-api.md`](./contracts/conversion-preview-api.md) — Server-side preview endpoint called by the quote form.

Public agreement-PDF behavior is governed by the existing `AgreementsController.Pdf` action; no new endpoint introduced — only its data contract changes (a new conversion-note section in the rendered HTML).

### Quickstart

[quickstart.md](./quickstart.md) is a developer guide: how to run AppHost, seed an initial rate via the admin UI (or a `dotnet run --project` snippet for tests), create a USD quote, generate a PDF, and inspect the database state.

### Constitution re-check (post-design)

| Principle | Status | Notes |
|---|---|---|
| I. Clean Architecture | PASS | No leaks introduced by contracts. Conversion logic stays in Application/Infrastructure; controllers thin. |
| II. Rich Domain Model | PASS | `ExchangeRate` validates on construction (positive buy/sell, no future-dated, no duplicate timestamp); `MarkUsed` is the only mutator and is one-way. `SupplierQuote.EditAmount` re-applies snapshot internally. |
| III. End-to-End Testing | PASS | Seven story-aligned Playwright classes specified above. |
| IV. Schema-First DB | PASS | All schema in `FundingPlatform.Database`. Post-deploy seed script. |
| V. Spec-Driven | PASS | This plan ties back to spec FR-IDs throughout. |
| VI. Simplicity | PASS | No new abstractions beyond what the feature demands. No premature caching. No unused configurability. |

**Verdict**: PASS post-design.

## Complexity Tracking

> No constitution violations. Table intentionally empty.

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| (none) | (n/a) | (n/a) |
