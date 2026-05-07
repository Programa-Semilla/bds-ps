---

description: "Task list for spec 015 — Suppliers Quotes Multi-Currency"
---

# Tasks: Suppliers Quotes Multi-Currency

**Input**: Design documents from `/specs/015-multi-currency-quotes/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/

**Tests**: E2E Playwright tests are mandatory per Constitution III; unit and integration tests are added per task description.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each story.

**Naming**: User-facing language in spec.md says "supplier quote". The codebase entity is `Quotation` (singular) under `src/FundingPlatform.Domain/Entities/Quotation.cs`. All file paths below use the codebase names.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: Which user story this task belongs to (US1–US6, mapping to spec.md)
- File paths are repo-relative.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Confirm the branch is clean and the build is green before changes begin.

- [ ] T001 Confirm `dotnet build FundingPlatform.slnx` is green from a clean checkout of branch `015-multi-currency-quotes` before any code changes
- [ ] T002 Confirm `dotnet run --project src/FundingPlatform.AppHost` boots the dev stack cleanly so the migration tests in Phase 2 have a target to deploy against

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Schema, entities, and the conversion seam every user story depends on.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete

### Database (dacpac, schema-first per Constitution IV)

- [ ] T010 Add `src/FundingPlatform.Database/Tables/dbo.Currencies.sql` with the columns, CHECK constraints, and filtered unique index defined in data-model.md
- [ ] T011 Add `src/FundingPlatform.Database/Tables/dbo.ExchangeRates.sql` with columns, FKs to `dbo.Currencies`, CHECK constraints, the unique index `UQ_ExchangeRates_PairAt`, and the descending lookup index `IX_ExchangeRates_PairEffectiveAtDesc` (inline where existing tables put indexes inline; otherwise as separate `Indexes/` files matching project convention)
- [ ] T013 Modify `src/FundingPlatform.Database/Tables/dbo.Quotations.sql` to add `ConvertedCrcAmount`, `SnapshotRateValue`, `SnapshotRateType`, `SnapshotEffectiveAtUtc`, `SnapshotRateId` (FK to `dbo.ExchangeRates(Id)` `ON DELETE NO ACTION`), `LegacyNeedsReview`, and the two CHECK constraints from data-model.md. Existing `Currency NVARCHAR(3) NULL` column is tightened to `CHAR(3) NOT NULL` with FK to `dbo.Currencies(Code)` (the post-deploy MERGE in T016 backfills `'CRC'` first to satisfy the NOT NULL transition). Existing `Price` column unchanged
- [ ] T014 [P] Add `src/FundingPlatform.Database/Tables/dbo.Quotations.sql` filtered index `IX_Quotations_LegacyNeedsReview` on `LegacyNeedsReview WHERE LegacyNeedsReview = 1` (inline or under `Indexes/`)
- [ ] T015 [P] Add index `IX_Quotations_SnapshotRateId` (inline or under `Indexes/`) for the FK lookup
- [ ] T016 Extend `src/FundingPlatform.Database/PostDeployment/SeedData.sql` (idempotent) to: (a) `MERGE` CRC (base, enabled, displayOrder=1) and USD (enabled, displayOrder=2) into `dbo.Currencies`; (b) backfill `dbo.Quotations.Currency` with `'CRC'` where NULL; (c) for any existing row where `Currency = 'CRC'`: set `ConvertedCrcAmount = Price`; for any existing row where `Currency <> 'CRC'` AND `Snapshot* are NULL`: set `LegacyNeedsReview = 1`. All three blocks must be no-ops on re-deploy (`WHERE … AND ConvertedCrcAmount IS NULL` style guards)
- [ ] T017 Verify dacpac build (`dotnet build src/FundingPlatform.Database`) and AppHost dev run deploys the new schema cleanly on a fresh container

### Domain (Constitution II — rich model)

- [ ] T020 [P] Add `src/FundingPlatform.Domain/ValueObjects/CurrencyCode.cs` (record with `Crc`/`Usd` static instances and `IsBase`)
- [ ] T021 [P] Add `src/FundingPlatform.Domain/Entities/Currency.cs` (entity with `Enable`/`Disable` behavior; `Disable` throws `InvalidOperationException` when `IsBaseCurrency` is true)
- [ ] T022 [P] Add `src/FundingPlatform.Domain/Enums/RateType.cs` (`enum { Buy = 1, Sell = 2 }`)
- [ ] T023 [P] Add `src/FundingPlatform.Domain/ValueObjects/ExchangeRateSnapshot.cs` (immutable record: `RateRecordId`, `RateValue`, `RateType`, `EffectiveAtUtc`)
- [ ] T024 Add `src/FundingPlatform.Domain/Entities/ExchangeRate.cs` (entity). Constructor validates: positive buy, positive sell, distinct pair, `EffectiveAtUtc <= now`. `MarkUsed()` is one-way idempotent. `ConvertUsdToCrc(decimal usd)` performs `Math.Round(usd * BuyRate, 2, MidpointRounding.AwayFromZero)`. `ToSnapshot(RateType)` projects to `ExchangeRateSnapshot`
- [ ] T025 Modify `src/FundingPlatform.Domain/Entities/Quotation.cs` to add `ConvertedCrcAmount`, `Snapshot`, `LegacyNeedsReview`, plus methods `SetCurrencyAndAmount(CurrencyCode, decimal price, IConversionService)`, `EditAmount(decimal newPrice)` (re-applies existing `Snapshot`; throws if `Snapshot is null && Currency != "CRC"`), `ChangeCurrency(CurrencyCode newCurrency, IConversionService)` (clears existing snapshot and re-applies a new one — implements FR-017a re-conversion), `AttachLegacyRate(ExchangeRateSnapshot, decimal convertedCrc)` (clears `LegacyNeedsReview`). The existing `Currency` string + `Price` decimal stay; new code paths route through `SetCurrencyAndAmount`. The existing `EditCurrency(string)` method is left in place for callers in legacy code paths but is annotated `[Obsolete("Use ChangeCurrency to reset the rate snapshot.", error: false)]` to discourage new use

### Application (interfaces + use-case helpers)

- [ ] T030 [P] Add `src/FundingPlatform.Application/Interfaces/ICurrencyConfigService.cs` with `Task EnableAsync(CurrencyCode)`, `Task DisableAsync(CurrencyCode)`, `Task<IReadOnlyList<Currency>> ListEnabledAsync()`, `Task<IReadOnlyList<Currency>> ListAllAsync()`
- [ ] T031 [P] Add `src/FundingPlatform.Application/Interfaces/IExchangeRateService.cs` with `Task<ExchangeRate> CreateAsync(CurrencyCode source, CurrencyCode target, decimal buy, decimal sell, DateTime effectiveAtUtc, string actorUserId)` and `Task<IReadOnlyList<ExchangeRate>> ListAsync(CurrencyCode source, CurrencyCode target)`
- [ ] T032 [P] Add `src/FundingPlatform.Application/Interfaces/IConversionService.cs` with `Task<ConversionResult> ConvertAsync(CurrencyCode source, CurrencyCode target, decimal amount)` returning `(decimal Converted, ExchangeRateSnapshot Snapshot, ExchangeRate Source)` and a typed `MissingRateException` (under `src/FundingPlatform.Application/Errors/`) for FR-018
- [ ] T033 Extend `src/FundingPlatform.Application/Errors/UserFacingErrorCode.cs` with `MissingExchangeRate`, `CurrencyDisabled`, `RateImmutableUseSupersede`, `DuplicateRateTimestamp`, `FutureDatedRateRejected` codes mapped to the spec's user-facing messages (es-CR strings live in resx)
- [ ] T034 Add `src/FundingPlatform.Application/DTOs/ConversionPreviewDto.cs` matching the response shape in `contracts/conversion-preview-api.md`
- [ ] T035 Add audit-log event-type string constants `Currency.Enabled`, `Currency.Disabled`, `ExchangeRate.Created`, `ExchangeRate.EditAttemptBlocked`, `ExchangeRate.DeleteAttemptBlocked`, `Quotation.LegacyRateAttached` to wherever existing audit-event constants live in `src/FundingPlatform.Application` (search the existing `*Service.cs` files for the current pattern; e.g., spec 002/spec 009 added admin events)

### Infrastructure (EF mapping + conversion impl)

- [ ] T040 [P] Add `src/FundingPlatform.Infrastructure/Persistence/Configurations/CurrencyConfiguration.cs` (table `Currencies`, PK `Code`, `RowVersion` token)
- [ ] T041 [P] Add `src/FundingPlatform.Infrastructure/Persistence/Configurations/ExchangeRateConfiguration.cs` (table `ExchangeRates`, decimal precisions per data-model.md, FKs, `RowVersion` token, configures the unique index)
- [ ] T042 Modify `src/FundingPlatform.Infrastructure/Persistence/Configurations/QuotationConfiguration.cs` to (a) keep existing `Currency` mapping as `char(3) NOT NULL`, (b) map `Price` decimal precision unchanged, (c) add `ConvertedCrcAmount` decimal(18,2) nullable, (d) map the embedded `ExchangeRateSnapshot` value object via `OwnsOne` (or column-by-column mapping if `OwnsOne` collides with the EF Core 10 conventions used elsewhere in the codebase — check `FundingAgreementConfiguration.cs` for the established pattern), (e) map `LegacyNeedsReview` bit
- [ ] T043 Modify `src/FundingPlatform.Infrastructure/Persistence/AppDbContext.cs` to add `DbSet<Currency>` and `DbSet<ExchangeRate>` and register the new configurations alongside existing ones
- [ ] T044 Add `src/FundingPlatform.Infrastructure/Persistence/Repositories/ExchangeRateRepository.cs` (or a new `Services/ConversionService.cs` — match whatever folder hosts existing reads): exposes the latest-rate query and the create-with-duplicate-timestamp-handling path. Catches `DbUpdateException` whose inner `SqlException.Number` is `2627` or `2601` and translates to `DuplicateRateTimestamp` per FR-007. Inject this into `IExchangeRateService`
- [ ] T045 Add `src/FundingPlatform.Infrastructure/Services/ConversionService.cs` (or co-locate with the rate repository in `Persistence/Services` — match existing pattern, e.g., where `ApplicantDashboardProjection`'s implementation lives) implementing `IConversionService`: queries `dbo.ExchangeRates` `TOP 1 … ORDER BY EffectiveAtUtc DESC` for the requested pair, throws `MissingRateException` if none, applies `ExchangeRate.ConvertUsdToCrc`, returns `ConversionResult` with the snapshot
- [ ] T046 [P] Register the new services in DI inside `src/FundingPlatform.Web/Program.cs` alongside the existing application-service registrations: `AddScoped<ICurrencyConfigService, CurrencyConfigService>()`, `AddScoped<IExchangeRateService, ExchangeRateService>()`, `AddScoped<IConversionService, ConversionService>()`. Wire `LegacyQuotationRateAttachService` (US6) here too in the same task to avoid revisiting Program.cs later

### Foundational tests

- [ ] T050 [P] Add `tests/FundingPlatform.Tests.Unit/ExchangeRateTests.cs` covering: positive-buy/sell invariants, distinct-pair invariant, future-dated rejection, `MarkUsed` idempotency, `ConvertUsdToCrc` rounding cases (0.005 rounds away from zero, 0.004 stays — pin known examples)
- [ ] T051 [P] Add `tests/FundingPlatform.Tests.Unit/ConversionServiceTests.cs` covering: missing-rate throws `MissingRateException`, picks the latest by `EffectiveAtUtc`, returns snapshot consistent with chosen rate (use an in-memory test double for the rate query)
- [ ] T052 [P] Add `tests/FundingPlatform.Tests.Integration/ExchangeRateRepositoryTests.cs` (real SQL via `AspireFixture`): unique-index conflict surfaces as the FR-007 validation error; latest-rate query returns most recent
- [ ] T053 [P] Add `tests/FundingPlatform.Tests.Integration/MigrationTests.cs`: deploy dacpac to a clean DB; assert seed inserts CRC + USD; pre-insert simulated legacy rows (one CRC, one USD without snapshot) BEFORE re-running the post-deploy block, then assert CRC rows are stamped (`ConvertedCrcAmount = Price`, `LegacyNeedsReview = 0`) and non-CRC rows are flagged (`LegacyNeedsReview = 1`, `ConvertedCrcAmount IS NULL`); re-run idempotency assertion (re-deploying does not re-flag already-attached quotations)

**Checkpoint**: Foundation ready — schema deployed, conversion seam wired, audit-event constants live, DI registered. User stories can now begin in parallel.

---

## Phase 3: User Story 1 — Applicant creates a USD `Quotation` with deterministic CRC conversion (Priority: P1) 🎯 MVP

**Goal**: An applicant on the supplier-quote form can pick USD, see the CRC preview update in real time, save, and have the rate snapshot persisted on the `Quotation` row.

**Independent Test**: With one published CRC↔USD rate present, the applicant on `/Application/{appId}/Item/{itemId}/Quotation/Add` types `1000` USD, sees `₡520,000.00` (assuming Buy=520) update under the input, saves, and the application detail page shows both `1000 USD` and `₡520,000.00 CRC` plus the rate-snapshot read-only box.

### Tests for User Story 1

- [ ] T100 [P] [US1] Add `tests/FundingPlatform.Tests.Unit/QuotationCurrencyTests.cs` covering: `SetCurrencyAndAmount` snapshots a USD rate; `EditAmount` re-applies the snapshot; `ChangeCurrency` clears + re-snapshots (FR-017a); `AttachLegacyRate` clears `LegacyNeedsReview`
- [ ] T101 [P] [US1] Add `tests/FundingPlatform.Tests.Integration/QuotationCreateUsdTests.cs` against `AspireFixture`: creates a USD quotation via `ApplicationService.AddQuotationToExistingBranchAsync` (existing public surface, extended to route through `Quotation.SetCurrencyAndAmount`), asserts row in DB has `Currency='USD'`, `Price=1000`, `ConvertedCrcAmount=520000.00`, snapshot fields populated, `dbo.ExchangeRates.IsUsed = 1`
- [ ] T102 [P] [US1] Add `tests/FundingPlatform.Tests.E2E/ApplicantUsdQuoteE2E.cs` Playwright class — page-object for the quote form (`AddQuotationPage`) at the existing route `/Application/{appId}/Item/{itemId}/Quotation/Add`. Drives the golden path AND the "no rate published → blocking validation" failure path (FR-018, surface the literal Spanish message in resx)

### Implementation for User Story 1

- [ ] T110 [US1] Modify `src/FundingPlatform.Web/Controllers/QuotationController.cs` to add `[HttpPost("Convert")][Authorize(Roles = "Applicant")][ValidateAntiForgeryToken] public async Task<IActionResult> Convert(int appId, int itemId, ConversionPreviewRequestModel req)` returning the JSON shape from `contracts/conversion-preview-api.md`. Maps `MissingRateException` → 409 with the FR-018 message. Maps unknown/disabled currency → 404/400 per contract
- [ ] T111 [US1] Modify `QuotationController.Add(POST)` and the underlying `ApplicationService.AddQuotationToExistingBranchAsync` so that on save the application service constructs/loads the `Item`, then calls `Quotation.SetCurrencyAndAmount(CurrencyCode.From(model.Currency), model.Price, conversionService)` instead of the legacy free-text Currency wiring. Maps `MissingRateException` to a model-state validation error so the form re-renders inline with the FR-018 message
- [ ] T112 [P] [US1] Add `src/FundingPlatform.Web/wwwroot/js/quote-conversion-preview.js` — debounced (300 ms) blur handler that POSTs to `Convert` (full URL composed from current page route), updates the preview region with `convertedCrc` + rate metadata, hides the preview region when the selector switches to CRC. **Never multiplies client-side.**
- [ ] T113 [US1] Modify the existing Add-Quotation Razor view (locate via `dotnet new mvc`-style routing — `src/FundingPlatform.Web/Views/Quotation/Add.cshtml`) to: (a) replace the free-text currency input with a `<select>` populated from `ICurrencyConfigService.ListEnabledAsync()` defaulting to CRC; (b) add the preview region (initially hidden); (c) reference `quote-conversion-preview.js` and the anti-forgery token; (d) preserve all existing fields (file upload, ValidUntil, supplier name)
- [ ] T114 [US1] Modify the existing Application Details view (`src/FundingPlatform.Web/Views/Application/Details.cshtml` — confirm exact path) so the per-Item quotation rows render via the new `MoneyDisplayViewComponent` (added in US4). For US1's MVP slice the row can render `Original {Currency} {Price}` + raw `ConvertedCrcAmount` text with a placeholder until US4 lands the polished component
- [ ] T115 [US1] Run `tests/FundingPlatform.Tests.E2E/ApplicantUsdQuoteE2E.cs` against AppHost; ensure both scenarios pass

**Checkpoint**: User Story 1 fully functional and demonstrable. The MVP slice is complete (with one rate seeded directly via `dbo.ExchangeRates` SQL or the US3 admin UI before US3 ships).

---

## Phase 4: User Story 2 — Applicant creates a CRC `Quotation` (Priority: P1)

**Goal**: Existing CRC-only flow continues to work — currency selector defaults to CRC, no preview area appears, no snapshot persisted.

**Independent Test**: An applicant picks CRC on the form, enters `750000`, saves, and the quotation row shows `₡750,000.00 CRC` only with no conversion-applied indicator anywhere downstream.

### Tests for User Story 2

- [ ] T200 [P] [US2] Add `tests/FundingPlatform.Tests.Integration/QuotationCreateCrcTests.cs`: creates a CRC quotation via `ApplicationService`, asserts `Currency='CRC'`, `ConvertedCrcAmount = Price`, snapshot fields NULL, `LegacyNeedsReview = 0`
- [ ] T201 [P] [US2] Add `tests/FundingPlatform.Tests.E2E/ApplicantCrcQuoteE2E.cs` Playwright class: golden path; assert preview region remains hidden; assert quotation row in Application Details shows no conversion indicator

### Implementation for User Story 2

- [ ] T210 [US2] Verify `Quotation.SetCurrencyAndAmount` short-circuits when `currency == Crc` (Original=Converted=price, Snapshot=null, Legacy=false) — already in T025 but add unit-test coverage in `QuotationCurrencyTests.cs`
- [ ] T211 [US2] Verify `quote-conversion-preview.js` hides/clears the preview region on switch to CRC (covered by T112 if implemented carefully; this task adds an explicit Playwright assertion in `ApplicantCrcQuoteE2E.cs`)
- [ ] T212 [US2] Run `ApplicantCrcQuoteE2E.cs` and confirm pass

**Checkpoint**: US1 + US2 both green. Applicant flow end-to-end stable.

---

## Phase 5: User Story 3 — Administrator manages enabled currencies and exchange rates (Priority: P1)

**Goal**: An administrator can toggle USD enable/disable (CRC permanently locked) and publish CRC↔USD reference rates with buy + sell + effective timestamp. Rates are immutable once used.

**Independent Test**: The admin loads `/Admin/AdminCurrencies`, disables USD, re-enables it; loads `/Admin/AdminExchangeRates`, publishes a new rate, sees it become active; attempts to edit/delete a used rate and sees the FR-008 rejection.

### Tests for User Story 3

- [ ] T300 [P] [US3] Add `tests/FundingPlatform.Tests.Integration/CurrencyConfigServiceTests.cs`: enable/disable USD, attempt to disable CRC throws, audit-log events are written
- [ ] T301 [P] [US3] Add `tests/FundingPlatform.Tests.Integration/ExchangeRateServiceTests.cs`: create accepts valid input, rejects zero/negative, rejects future-dated (FR-007a), rejects duplicate (FR-007); attempt to edit/delete via the public surface always blocked once `IsUsed=1`; audit events on every create + every blocked attempt
- [ ] T302 [P] [US3] Add `tests/FundingPlatform.Tests.E2E/AdminCurrencyConfigE2E.cs` (admin currency page-object, scenarios: toggle USD, attempt-disable CRC error message)
- [ ] T303 [P] [US3] Add `tests/FundingPlatform.Tests.E2E/AdminExchangeRateE2E.cs` (rate page-object, scenarios: create valid rate, validate zero-buy/zero-sell/duplicate-timestamp/future-dated rejections, view history list, attempt-edit-used-rate rejection)

### Implementation for User Story 3

- [ ] T310 [P] [US3] Implement `src/FundingPlatform.Application/Services/CurrencyConfigService.cs`: enforces CRC-permanent invariant, writes audit events
- [ ] T311 [P] [US3] Implement `src/FundingPlatform.Application/Services/ExchangeRateService.cs`: validates per FR-006 / FR-007 / FR-007a, catches the duplicate-key surface from `ExchangeRateRepository`, writes audit events on every create + every blocked attempt
- [ ] T312 [US3] Add `src/FundingPlatform.Web/Controllers/Admin/AdminCurrenciesController.cs` exposing the endpoints in `contracts/currency-api.md` (route: `/Admin/AdminCurrencies/...`). Apply `[Authorize(Roles = "Administrator")]` on the controller
- [ ] T313 [US3] Add `src/FundingPlatform.Web/Controllers/Admin/AdminExchangeRatesController.cs` exposing the endpoints in `contracts/exchange-rate-api.md`. PUT/DELETE return `405 Method Not Allowed` and write `ExchangeRate.EditAttemptBlocked` / `DeleteAttemptBlocked` audit events
- [ ] T314 [P] [US3] Add `src/FundingPlatform.Web/Views/Admin/Currencies/Index.cshtml` (Tabler-styled list with enable/disable toggles)
- [ ] T315 [P] [US3] Add `src/FundingPlatform.Web/Views/Admin/ExchangeRates/Index.cshtml` (history list with active-rate highlight)
- [ ] T316 [P] [US3] Add `src/FundingPlatform.Web/Views/Admin/ExchangeRates/Create.cshtml` (form, client-side validation matches server rules)
- [ ] T317 [US3] Wire navigation links into the existing admin sidebar/menu (consistent with how spec 009 added other admin pages — search the current `_AdminLayout.cshtml` partial)
- [ ] T318 [US3] Run all four US3 tests against AppHost; ensure pass

**Checkpoint**: US1, US2, US3 all green. The applicant flow now works end-to-end without dev-only DB seeding because admins can publish rates through the UI.

---

## Phase 6: User Story 4 — Reviewers, approvers, and dashboards display multi-currency clearly (Priority: P2)

**Goal**: Every quotation/total surface in the platform shows original + converted CRC and a conversion indicator with rate tooltip. Cross-currency request totals roll up in CRC.

**Independent Test**: A reviewer opens an Application with one CRC and one USD selected-supplier quotation across its Items and confirms each line shows original + CRC + tooltip; the application total equals the sum of converted-CRC values; CRC-only lines have no tooltip.

### Tests for User Story 4

- [ ] T400 [P] [US4] Add `tests/FundingPlatform.Tests.Integration/RequestTotalRollupTests.cs`: build an Application with mixed CRC+USD selected-supplier quotations across multiple Items, query the existing `ApplicantDashboardProjection` / `ReviewerQueueProjection` (extended), assert total = sum of converted-CRC, legacy-flagged quotations excluded
- [ ] T401 [P] [US4] Add `tests/FundingPlatform.Tests.E2E/ReviewerDisplayE2E.cs`: reviewer logs in, opens a mixed Application, asserts both lines + tooltip + total

### Implementation for User Story 4

- [ ] T410 [P] [US4] Add `src/FundingPlatform.Web/ViewComponents/MoneyDisplayViewComponent.cs` + `Default.cshtml`: takes `(decimal? original, CurrencyCode? originalCurrency, decimal? convertedCrc, ExchangeRateSnapshot? snapshot)`. CRC-only renders the CRC string; non-CRC renders `$1,000.00 USD` + `(₡520,000.00 CRC)` + indicator. Spanish-localized (es-CR) labels
- [ ] T411 [P] [US4] Add `src/FundingPlatform.Web/ViewComponents/ConversionIndicatorViewComponent.cs` + `Default.cshtml`: small ⓘ icon with `data-bs-toggle="tooltip"` carrying rate value + type + effective date
- [ ] T412 [US4] Update `src/FundingPlatform.Web/Views/Application/Details.cshtml` (and any partial it includes for per-Item quotation rows) to render every monetary cell via `MoneyDisplayViewComponent`. The Item's selected-supplier quotation determines the row total
- [ ] T413 [US4] Update the application-summary computed total to sum each Item's `SelectedSupplier`-chosen `Quotation.ConvertedCrcAmount` (excluding `LegacyNeedsReview = 1`) and display via `MoneyDisplayViewComponent` with `originalCurrency = null` so it renders pure CRC
- [ ] T414 [US4] Update `src/FundingPlatform.Application/Services/ApplicantDashboardProjection.cs` and `src/FundingPlatform.Application/Services/ReviewerQueueProjection.cs` to include both original + converted CRC fields on the row DTOs. Update the corresponding views (`ApplicantDashboard*`, `Reviewer*`) to render via the view components
- [ ] T415 [US4] Update the approval/review screen views from spec 002 (search `Views/Review/*.cshtml`) to render via `MoneyDisplayViewComponent`
- [ ] T416 [US4] Update admin reports (CSV streaming endpoint hosted by `AdminReportsController` per spec 010) to **append** three new columns at the end of every existing row (preserving prior column order for back-compat consumers): `OriginalCurrencyCode`, `OriginalAmount`, `ConvertedCrcAmount`. CRC-only rows leave `OriginalCurrencyCode = "CRC"` and `OriginalAmount = ConvertedCrcAmount`. Existing CSV row-limit (`AdminReports:CsvRowLimit`) is unchanged
- [ ] T417 [US4] Run `ReviewerDisplayE2E` and `RequestTotalRollupTests`; ensure pass

**Checkpoint**: US1–US4 green. Multi-currency now consistent across the entire authenticated UI surface.

---

## Phase 7: User Story 5 — Final agreement PDF shows CRC with conversion indicator (Priority: P2)

**Goal**: The PDF renders CRC only with a per-line conversion note when any quotation came from non-CRC; CRC-only requests render unchanged. PDF refuses on missing snapshot.

**Independent Test**: Generate a PDF for a mixed Application — verify CRC-only display + conversion notes; generate one for a CRC-only Application — verify visual identity to today's output; corrupt a snapshot to NULL and verify FR-027 inline error + log entry.

### Tests for User Story 5

- [ ] T500 [P] [US5] Add `tests/FundingPlatform.Tests.Integration/PdfRenderingTests.cs`: golden test for CRC-only PDF (compare extracted text/structure to a baseline checked into `tests/Fixtures/pdfs/`), mixed-currency PDF includes the per-line conversion note with rate value + Buy + effective date, missing-snapshot row triggers domain exception
- [ ] T501 [P] [US5] Add `tests/FundingPlatform.Tests.E2E/AgreementPdfMultiCurrencyE2E.cs`: drives the agreement page, downloads PDF, asserts file is non-empty + magic bytes; the inline error path asserts the user-visible message and that the application log contains the offending quotation id

### Implementation for User Story 5

- [ ] T510 [P] [US5] Modify the existing FundingAgreement Razor partial used by the PDF renderer (locate via `IFundingAgreementHtmlRenderer` impl in `Infrastructure` and the Razor view it consumes) to render the conversion note row beneath each non-CRC line: `Conversión: 1 USD = ₡<rate> (Tipo Compra, vigente desde <fecha>)`. Locale-aware formatting via existing `CultureInfo` helpers (es-CR primary, per CLAUDE.md and spec 012)
- [ ] T511 [US5] Modify `src/FundingPlatform.Infrastructure` PDF renderer (the implementation of `IFundingAgreementPdfRenderer`) to throw `MissingConversionMetadataException` (new domain exception in `src/FundingPlatform.Domain/Exceptions/`) when any line in the request has `Currency != 'CRC'` AND `Snapshot is null`. Otherwise feeds the partial with new view-model fields
- [ ] T512 [US5] Modify `src/FundingPlatform.Web/Controllers/FundingAgreementController.cs` (verify exact name) Pdf action to catch `MissingConversionMetadataException` and: (a) write a structured log entry containing the offending quotation ids; (b) **re-render the agreement view directly with the inline error in the model** (NOT a `TempData`-survive-redirect pattern, so a hard reload still shows the error until the offending quotation is fixed — per spec edge case "PDF refusal UX"). Other exceptions continue to propagate
- [ ] T513 [US5] Add a baseline PDF artifact under `tests/Fixtures/pdfs/crc-only-baseline.pdf` (committed) and a `tests/Fixtures/pdfs/mixed-baseline.expected.txt` extraction for the PdfRenderingTests golden assertions
- [ ] T514 [US5] Run all US5 tests; ensure pass

**Checkpoint**: US1–US5 green. The legally-meaningful PDF now reflects multi-currency reality.

---

## Phase 8: User Story 6 — Legacy `Quotation` rows flagged and quarantined (Priority: P3)

**Goal**: Pre-existing CRC quotations auto-stamp; pre-existing non-CRC quotations lacking conversion metadata are flagged `LegacyNeedsReview = 1`, excluded from totals, and admin can attach a historical rate to clear the flag.

**Independent Test**: Insert synthetic legacy quotations pre-deploy → re-deploy → confirm CRC rows are stamped, non-CRC are flagged. Open `/Admin/AdminLegacyQuotations`, attach a rate to one, confirm flag clears and row appears in cross-currency totals.

### Tests for User Story 6

- [ ] T600 [P] [US6] Add re-run idempotency assertion to `MigrationTests.cs` (running the post-deploy block twice does not re-flag already-attached quotations). (The base assertions are in T053; this task only adds the second-run assertion)
- [ ] T601 [P] [US6] Add `tests/FundingPlatform.Tests.Integration/LegacyQuotationRateAttachServiceTests.cs`: attaches a historical rate; asserts snapshot fields set, `ConvertedCrcAmount` computed, flag cleared, audit `Quotation.LegacyRateAttached` event written
- [ ] T602 [P] [US6] Add `tests/FundingPlatform.Tests.E2E/LegacyQuotationFlowE2E.cs`: log in as admin, navigate to legacy queue, attach rate, verify quotation now appears with conversion data in the Application detail view and the flag is gone

### Implementation for User Story 6

- [ ] T610 [US6] Add `src/FundingPlatform.Application/Services/LegacyQuotationRateAttachService.cs` implementing the attach use case (reads the user-selected `ExchangeRate`, calls `Quotation.AttachLegacyRate(...)`, writes the audit event, persists)
- [ ] T611 [US6] Add `src/FundingPlatform.Web/Controllers/Admin/AdminLegacyQuotationsController.cs` with `Index` (list flagged) and `POST AttachRate` (rate-id + quotation-id binding)
- [ ] T612 [US6] Add `src/FundingPlatform.Web/Views/Admin/LegacyQuotations/Index.cshtml`: filtered list, per-row "attach rate" form using the existing rate-history list as the rate picker
- [ ] T613 [US6] Wire admin nav link into `_AdminLayout.cshtml`
- [ ] T614 [US6] Run all US6 tests; ensure pass

**Checkpoint**: All six user stories green. End-to-end story coverage complete.

---

## Phase 9: Polish & Cross-Cutting Concerns

- [ ] T900 [P] Add es-CR resx strings for new admin/applicant copy (currency-disabled labels, conversion indicator tooltip, validation messages including the literal "No reference exchange rate is configured. Contact an administrator." per spec FR-018 — also add the canonical Spanish translation, e.g., "No hay tipo de cambio de referencia configurado. Contacte a un administrador.")
- [ ] T901 [P] Verify the Tabler styling is consistent with existing admin pages (no new CDN refs; vendor any new assets locally per CLAUDE.md "No CDN" rule)
- [ ] T902 Walk through `quickstart.md` end-to-end against a fresh AppHost run; fix any drift between docs and code (e.g., update route prefixes inside quickstart.md if T110 changed them)
- [ ] T903 Run the full E2E suite from a clean checkout: `dotnet test tests/FundingPlatform.Tests.E2E`. ALL tests must be green per Constitution III + project memory ("Delivery requires a personally-executed green E2E run")
- [ ] T904 Update `CLAUDE.md` "Active Technologies" / "Recent Changes" sections to mention spec 015 (multi-currency)
- [ ] T905 Final commit + push for the feature branch
- [ ] T906 Add `scripts/perf-baseline-015.ps1` (or `.sh`) following existing `scripts/` perf-baseline tooling: measures p95 of `POST /Application/{appId}/Item/{itemId}/Quotation/Convert` (target ≤ 200 ms), Quotation save with USD conversion (target ≤ 800 ms), and `POST /Admin/AdminExchangeRates` (target ≤ 500 ms). The thresholds are advisory non-blocking goals; the script reports current numbers and exits non-zero only on a >2× regression vs. baseline
- [ ] T907 Update `src/FundingPlatform.AppHost/appsettings.json` (or equivalent) so `FundingAgreement:CurrencyIsoCode` is set to `CRC` (the existing default of `COP` is leftover from an earlier draft and contradicts the platform base currency confirmed in this spec). Verify dev + E2E runs still produce a CRC PDF

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies.
- **Foundational (Phase 2)**: Depends on Setup. **BLOCKS** all user stories.
- **User Stories (Phases 3–8)**: All depend on Foundational. Within each phase, tests may run in parallel where marked [P]; implementation tasks within a story have intra-phase ordering noted by the file-path collisions.
- **Polish (Phase 9)**: Depends on all desired user stories.

### User Story Dependencies (operational, not test-isolation)

- **US1** depends on a published rate. In production this means US3 ships first; in tests, US1 seeds a rate directly.
- **US2** is independent of US3.
- **US3** is independent.
- **US4** integrates US1 + US2 visually but tests can stub data.
- **US5** integrates US1 + US2 + the existing PDF pipeline.
- **US6** depends on Foundational migration logic only.

### Within Each User Story

- E2E tests are mandatory per Constitution III and are the final gate of each story.
- Models before services (already enforced in Phase 2).
- Services before controllers/endpoints.
- Each story's implementation must be testable in isolation.

### Parallel Opportunities

- All Phase 2 entity files (T020–T024) can run in parallel.
- Foundational tests (T050–T053) all parallel.
- Once Foundational completes, US1+US2+US3 can be split across developers; US4–US6 follow once their dependencies land.
- Story-internal [P] tests can be authored in parallel with their corresponding implementation tasks (write test → fail → implement).

---

## Parallel Example: User Story 1

```bash
# Tests for US1 (parallel):
Task: "Add tests/FundingPlatform.Tests.Unit/QuotationCurrencyTests.cs covering currency + edit semantics (T100)"
Task: "Add tests/FundingPlatform.Tests.Integration/QuotationCreateUsdTests.cs (T101)"
Task: "Add tests/FundingPlatform.Tests.E2E/ApplicantUsdQuoteE2E.cs (T102)"

# Frontend asset and JS in parallel with controller wiring:
Task: "Add quote-conversion-preview.js (T112)"
Task: "Modify QuotationController to add Convert action (T110)"
```

---

## Implementation Strategy

### MVP First (User Story 1 only)

1. Complete Phase 1 + Phase 2.
2. Complete Phase 3 (US1).
3. STOP — demo USD quotation with admin-seeded rate (DB-direct).

### Incremental Delivery

1. Foundation → US3 (admin can publish rates) → US1 (USD quote) → US2 (CRC quote stable) → US4 (display polish) → US5 (PDF) → US6 (legacy cleanup).
2. Each phase independently shippable; the codebase compiles and all already-shipped stories remain green at every commit.

### Parallel Team Strategy

After Foundational:
- Developer A: US3 (admin)
- Developer B: US1 (applicant USD)
- Developer C: US2 (applicant CRC, low-effort, can roll into A or B)
- Once US1+US3 land: Developer D: US4 (display) and Developer E: US5 (PDF) in parallel.
- US6: any developer post-foundation; not on the critical path.

---

## Notes

- [P] tasks = different files, no incomplete dependency.
- File paths assume repo root `/mnt/D/repos/bds-ps-multi-currency`.
- Every user story is independently testable through its own E2E class.
- Commit at each story checkpoint; push at every Speckit checkpoint per project memory.
- Avoid: cross-story dependencies that break independent test runs; same-file [P] markers; introducing new managed (NuGet) dependencies (CLAUDE.md "New managed dependencies require spec approval").
