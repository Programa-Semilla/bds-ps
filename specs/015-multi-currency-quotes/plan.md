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
| II. Rich Domain Model | PASS | `ExchangeRate` exposes behavior (`ConvertUsdToCrc`, `MarkUsed`, validation invariants on construction). `Quotation` gains `SetCurrencyAndAmount`, `EditAmount`, `ChangeCurrency`, `AttachLegacyRate` (snapshot reapplied / cleared per method) — no anemic public setters. |
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

Aligned with the **existing** FundingPlatform layout (flat folders under `Domain/Entities`, `Domain/ValueObjects`, etc.; admin controllers under `Web/Controllers/Admin/`; dacpac tables prefixed `dbo.`). Directories that get new files are listed; existing directories not listed are unchanged.

```text
src/
├── FundingPlatform.Domain/
│   ├── Entities/
│   │   ├── Currency.cs                  # NEW. Code, Symbol, Name, DecimalPrecision, IsEnabled, IsBaseCurrency, DisplayOrder.
│   │   ├── ExchangeRate.cs              # NEW aggregate. Immutable-once-used. ConvertUsdToCrc, MarkUsed, ToSnapshot.
│   │   └── Quotation.cs                 # EXTENDED — adds ConvertedCrcAmount, Snapshot, LegacyNeedsReview, SetCurrencyAndAmount, EditAmount, ChangeCurrency, AttachLegacyRate. Existing Currency + Price columns stay.
│   ├── ValueObjects/
│   │   ├── CurrencyCode.cs              # NEW. record(string Value) with Crc/Usd statics; IsBase.
│   │   └── ExchangeRateSnapshot.cs      # NEW. Embedded value object on Quotation.
│   └── Enums/
│       └── RateType.cs                  # NEW. enum { Buy = 1, Sell = 2 }.
│
├── FundingPlatform.Application/
│   ├── Interfaces/
│   │   ├── ICurrencyConfigService.cs    # NEW
│   │   ├── IExchangeRateService.cs      # NEW
│   │   └── IConversionService.cs        # NEW. ConvertAsync(source, target, amount) -> (converted, snapshot). Throws MissingRateException.
│   ├── Services/
│   │   ├── CurrencyConfigService.cs     # NEW. Enforces CRC permanent invariant.
│   │   ├── ExchangeRateService.cs       # NEW. Validates positive, future-date, duplicate-timestamp; emits audit events.
│   │   └── LegacyQuotationRateAttachService.cs  # NEW. Admin attaches a historical rate to a flagged legacy Quotation.
│   ├── Errors/
│   │   └── (extend UserFacingErrorCode.cs with new MissingExchangeRate, CurrencyDisabled codes)
│   └── DTOs/
│       └── ConversionPreviewDto.cs      # NEW. Response shape for /Quotation/Convert.
│
├── FundingPlatform.Infrastructure/
│   ├── Persistence/
│   │   ├── Configurations/
│   │   │   ├── CurrencyConfiguration.cs        # NEW
│   │   │   ├── ExchangeRateConfiguration.cs    # NEW
│   │   │   └── QuotationConfiguration.cs       # EXTENDED — map new columns + ExchangeRateSnapshot via OwnsOne.
│   │   ├── Repositories/
│   │   │   └── ExchangeRateRepository.cs       # NEW. Latest-rate query, history list. Catches DbUpdateException 2627/2601 → FR-007.
│   │   └── AppDbContext.cs                     # EXTENDED — DbSet<Currency>, DbSet<ExchangeRate>; register configurations.
│   ├── Services/                                # if a Services/ folder doesn't exist yet, place under Persistence/Services or matching existing pattern
│   │   └── ConversionService.cs                # NEW. Implements IConversionService; queries latest rate.
│   └── Pdf/
│       └── (extend the existing FundingAgreement PDF renderer/Razor partial — emit conversion note when non-CRC line; throw MissingConversionMetadataException)
│
├── FundingPlatform.Web/
│   ├── Controllers/
│   │   ├── QuotationController.cs              # EXTENDED — add [HttpPost("Convert")] action returning ConversionPreviewDto JSON; wire SetCurrencyAndAmount on save in Add (replaces the current free-text Currency-string flow); inline FR-018 validation message.
│   │   └── FundingAgreementController.cs       # EXTENDED — Pdf action catches MissingConversionMetadataException, re-renders the agreement view with inline error (FR-027). [If the file is named differently, this task targets whichever controller hosts the Pdf action today.]
│   ├── Controllers/Admin/
│   │   ├── AdminCurrenciesController.cs        # NEW — endpoints from contracts/currency-api.md
│   │   ├── AdminExchangeRatesController.cs     # NEW — endpoints from contracts/exchange-rate-api.md
│   │   └── AdminLegacyQuotationsController.cs  # NEW — Index + POST AttachRate (US6)
│   ├── Views/Admin/
│   │   ├── Currencies/Index.cshtml             # NEW
│   │   ├── ExchangeRates/{Index,Create}.cshtml # NEW
│   │   └── LegacyQuotations/Index.cshtml       # NEW
│   ├── ViewComponents/
│   │   ├── MoneyDisplayViewComponent.cs        # NEW — shared formatter
│   │   └── ConversionIndicatorViewComponent.cs # NEW — ⓘ tooltip
│   ├── ViewModels/
│   │   ├── ConversionPreviewRequestModel.cs    # NEW — for POST /Quotation/{*}/Convert
│   │   └── (extend AddQuotationViewModel with Currency selector populated from enabled currencies)
│   └── wwwroot/js/
│       └── quote-conversion-preview.js         # NEW — debounced blur handler; never multiplies client-side.
│
├── FundingPlatform.Database/                   # dacpac — single source of truth
│   ├── Tables/
│   │   ├── dbo.Currencies.sql                  # NEW
│   │   ├── dbo.ExchangeRates.sql               # NEW
│   │   └── dbo.Quotations.sql                  # ALTERED — keep Currency + Price; add ConvertedCrcAmount, SnapshotRateValue, SnapshotRateType, SnapshotEffectiveAtUtc, SnapshotRateId (FK NO ACTION), LegacyNeedsReview; add CHECK constraints; tighten Currency to NOT NULL char(3) with FK after migration.
│   ├── Indexes/                                # NEW directory if missing; spec 013 may already use inline index DDL inside table sql — match existing convention.
│   │   ├── IX_ExchangeRates_PairEffectiveAtDesc.sql
│   │   ├── IX_Quotations_LegacyNeedsReview.sql
│   │   └── IX_Quotations_SnapshotRateId.sql
│   └── PostDeployment/                         # existing folder; extend SeedData.sql (or add a new ordered include)
│       └── SeedData.sql                        # EXTENDED — idempotent MERGE for CRC + USD, plus the legacy-stamping/flagging block per FR-031/FR-032.
│
└── (no changes outside above)

tests/
├── FundingPlatform.Tests.Unit/
│   ├── ExchangeRateTests.cs                    # NEW — invariants, immutable-once-used, ConvertUsdToCrc rounding.
│   ├── ConversionServiceTests.cs               # NEW — decimal arithmetic, latest-rate selection, MissingRateException.
│   └── QuotationCurrencyTests.cs               # NEW — SetCurrencyAndAmount, EditAmount snapshot reapply, ChangeCurrency clears+re-snapshots, AttachLegacyRate clears flag.
├── FundingPlatform.Tests.Integration/
│   ├── ExchangeRateRepositoryTests.cs          # NEW — unique-index conflict, latest-rate query.
│   ├── CurrencyConfigServiceTests.cs           # NEW — enable/disable + audit.
│   ├── ExchangeRateServiceTests.cs             # NEW — full FR-006 / FR-007 / FR-007a coverage + audit.
│   ├── QuotationCreateUsdTests.cs              # NEW — application-layer save persists Original/Converted/Snapshot.
│   ├── QuotationCreateCrcTests.cs              # NEW — CRC quote keeps no snapshot.
│   ├── MigrationTests.cs                       # NEW — post-deploy stamps CRC, flags legacy non-CRC, idempotent on re-deploy.
│   ├── LegacyQuotationRateAttachServiceTests.cs # NEW (US6).
│   └── PdfRenderingTests.cs                    # NEW — CRC-only baseline, mixed-with-note, missing-snapshot refusal.
└── FundingPlatform.Tests.E2E/
    ├── AdminCurrencyConfigE2E.cs               # US3
    ├── AdminExchangeRateE2E.cs                 # US3
    ├── ApplicantUsdQuoteE2E.cs                 # US1
    ├── ApplicantCrcQuoteE2E.cs                 # US2
    ├── ReviewerDisplayE2E.cs                   # US4
    ├── AgreementPdfMultiCurrencyE2E.cs         # US5
    └── LegacyQuotationFlowE2E.cs               # US6
```

**Structure Decision**: Adopt the existing FundingPlatform layout (flat `Domain/Entities/`, `Domain/ValueObjects/`, `Domain/Enums/`; `Application/Services/` + `Application/Interfaces/`; `Infrastructure/Persistence/Configurations/`; admin controllers under `Web/Controllers/Admin/` and admin views under `Web/Views/Admin/`). The dacpac (`FundingPlatform.Database`) owns schema, with seeds in `PostDeployment/SeedData.sql`. Tests run under `AspireFixture` for integration + E2E.

**Naming bridge**: User-facing language in spec.md says "supplier quote". Code-facing language in this plan says **Quotation** because that is the existing entity (`src/FundingPlatform.Domain/Entities/Quotation.cs`). Each `Quotation` is one supplier's price for one `Item`; an `Item` belongs to an `Application` (the funding request). Spec FR-022 "request totals" is computed by summing each `Item.SelectedSupplier`'s chosen `Quotation.ConvertedCrcAmount` across the `Application`.

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
| II. Rich Domain Model | PASS | `ExchangeRate` validates on construction (positive buy/sell, no future-dated, no duplicate timestamp); `MarkUsed` is the only mutator and is one-way. `Quotation.EditAmount` re-applies snapshot internally; `ChangeCurrency` clears+re-snapshots. |
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
