---

description: "Task list for spec 015 — Suppliers Quotes Multi-Currency"
---

# Tasks: Suppliers Quotes Multi-Currency

**Input**: Design documents from `/specs/015-multi-currency-quotes/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/

**Tests**: E2E Playwright tests are mandatory per Constitution III; unit and integration tests are added per task description.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each story.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: Which user story this task belongs to (US1–US6, mapping to spec.md)
- All paths absolute or repo-relative as shown in plan.md project structure.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Create the new domain folders and confirm the branch/dacpac wiring works.

- [ ] T001 Create new domain folders `src/FundingPlatform.Domain/Currencies/` and `src/FundingPlatform.Domain/ExchangeRates/` and corresponding application folders `src/FundingPlatform.Application/Currencies/` and `src/FundingPlatform.Application/ExchangeRates/`
- [ ] T002 [P] Add new files placeholder `src/FundingPlatform.Web/Areas/Admin/Views/Currencies/_ViewImports.cshtml` and `src/FundingPlatform.Web/Areas/Admin/Views/ExchangeRates/_ViewImports.cshtml` (Tabler-styled, mirroring the existing admin area conventions)
- [ ] T003 Confirm `dotnet build FundingPlatform.slnx` is green from a clean checkout of branch `015-multi-currency-quotes` before any code changes

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Schema, entities, and the conversion seam every user story depends on.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete

### Database (dacpac, schema-first per Constitution IV)

- [ ] T010 Add `src/FundingPlatform.Database/Tables/Currencies.sql` with the columns, CHECK constraints, and filtered unique index defined in data-model.md
- [ ] T011 Add `src/FundingPlatform.Database/Tables/ExchangeRates.sql` with columns, FKs to `Currencies`, CHECK constraints, and unique index `UQ_ExchangeRates_PairAt`
- [ ] T012 Add `src/FundingPlatform.Database/Indexes/IX_ExchangeRates_PairEffectiveAtDesc.sql`
- [ ] T013 Modify `src/FundingPlatform.Database/Tables/SupplierQuotes.sql` to add `OriginalCurrencyCode`, `OriginalAmount`, `ConvertedCrcAmount`, `SnapshotRateValue`, `SnapshotRateType`, `SnapshotEffectiveAtUtc`, `SnapshotRateId` (FK NO ACTION), `LegacyNeedsReview` plus the two CHECK constraints from data-model.md
- [ ] T014 [P] Add `src/FundingPlatform.Database/Indexes/IX_SupplierQuotes_LegacyNeedsReview.sql` (filtered)
- [ ] T015 [P] Add `src/FundingPlatform.Database/Indexes/IX_SupplierQuotes_SnapshotRateId.sql`
- [ ] T016 Add post-deploy script `src/FundingPlatform.Database/PostDeploy/015-currency-seed.sql` that idempotently MERGEs CRC (base, enabled, displayOrder=1) and USD (enabled, displayOrder=2). Migration logic for stamping existing CRC quotes vs. flagging legacy non-CRC quotes is wired here as a separate idempotent block (used by US6 — keep guarded by `WHERE OriginalCurrencyCode IS NULL` so re-deploys are no-ops)
- [ ] T017 Verify dacpac build (`dotnet build src/FundingPlatform.Database`) and AppHost dev run deploys the new schema cleanly

### Domain (Constitution II — rich model)

- [ ] T020 [P] Add `src/FundingPlatform.Domain/Currencies/CurrencyCode.cs` (record with `Crc`/`Usd` static instances and `IsBase`)
- [ ] T021 [P] Add `src/FundingPlatform.Domain/Currencies/Currency.cs` (entity with `Enable`/`Disable` behavior; `Disable` throws `InvalidOperationException` when `IsBaseCurrency` is true)
- [ ] T022 [P] Add `src/FundingPlatform.Domain/ExchangeRates/RateType.cs` (`enum { Buy = 1, Sell = 2 }`)
- [ ] T023 [P] Add `src/FundingPlatform.Domain/ExchangeRates/ExchangeRateSnapshot.cs` (immutable record: `RateRecordId`, `RateValue`, `RateType`, `EffectiveAtUtc`)
- [ ] T024 Add `src/FundingPlatform.Domain/ExchangeRates/ExchangeRate.cs` (entity). Constructor validates: positive buy, positive sell, distinct pair, `EffectiveAtUtc <= now`. `MarkUsed()` is one-way idempotent. `ConvertUsdToCrc(decimal usd)` performs `Math.Round(usd * BuyRate, 2, MidpointRounding.AwayFromZero)`. `ToSnapshot(RateType)` projects to `ExchangeRateSnapshot`
- [ ] T025 Modify `src/FundingPlatform.Domain/SupplierQuotes/SupplierQuote.cs` to add `OriginalCurrency`, `OriginalAmount`, `ConvertedCrcAmount`, `Snapshot`, `LegacyNeedsReview`, plus methods `SetCurrencyAndAmount(CurrencyCode, decimal, IConversionService)`, `EditAmount(decimal)` (re-applies existing `Snapshot`; throws if `Snapshot is null && OriginalCurrency != Crc`), `AttachLegacyRate(ExchangeRateSnapshot, decimal convertedCrc)` (clears `LegacyNeedsReview`)

### Application (interfaces and use-case helpers)

- [ ] T030 [P] Add `src/FundingPlatform.Application/Currencies/ICurrencyConfigService.cs` with `Task EnableAsync(CurrencyCode)`, `Task DisableAsync(CurrencyCode)`, `Task<IReadOnlyList<Currency>> ListAsync()`
- [ ] T031 [P] Add `src/FundingPlatform.Application/ExchangeRates/IExchangeRateService.cs` with `Task<ExchangeRate> CreateAsync(CurrencyCode source, CurrencyCode target, decimal buy, decimal sell, DateTime effectiveAtUtc, string actorUserId)` and `Task<IReadOnlyList<ExchangeRate>> ListAsync(CurrencyCode source, CurrencyCode target)`
- [ ] T032 [P] Add `src/FundingPlatform.Application/ExchangeRates/IConversionService.cs` with `Task<ConversionResult> ConvertAsync(CurrencyCode source, CurrencyCode target, decimal amount)` returning `(decimal Converted, ExchangeRateSnapshot Snapshot)` and a typed `MissingRateException` for FR-018
- [ ] T033 Add audit-log event-type string constants `Currency.Enabled`, `Currency.Disabled`, `ExchangeRate.Created`, `ExchangeRate.EditAttemptBlocked`, `ExchangeRate.DeleteAttemptBlocked`, `SupplierQuote.LegacyRateAttached` to the existing audit-event constants location used elsewhere in `src/FundingPlatform.Application`

### Infrastructure (EF mapping + conversion impl)

- [ ] T040 [P] Add `src/FundingPlatform.Infrastructure/Persistence/Configurations/CurrencyConfiguration.cs` (table `Currencies`, PK `Code`, `RowVersion` token)
- [ ] T041 [P] Add `src/FundingPlatform.Infrastructure/Persistence/Configurations/ExchangeRateConfiguration.cs` (table `ExchangeRates`, decimal precisions, FKs, `RowVersion` token, configures unique index)
- [ ] T042 Modify `src/FundingPlatform.Infrastructure/Persistence/Configurations/SupplierQuoteConfiguration.cs` to map the new columns + the embedded `ExchangeRateSnapshot` value object via `OwnsOne` (or column mapping if owned types are awkward) and the `LegacyNeedsReview` flag
- [ ] T043 Modify `src/FundingPlatform.Infrastructure/Persistence/ApplicationDbContext.cs` to add `DbSet<Currency>` and `DbSet<ExchangeRate>` and register the configurations
- [ ] T044 Add `src/FundingPlatform.Infrastructure/Conversion/ConversionService.cs` implementing `IConversionService`: queries `ExchangeRates` `TOP 1 ... ORDER BY EffectiveAtUtc DESC`, throws `MissingRateException` if none, applies `ExchangeRate.ConvertUsdToCrc`, returns the snapshot
- [ ] T045 [P] Register the new services in DI: extend `src/FundingPlatform.Web/Program.cs` to add `services.AddScoped<ICurrencyConfigService, …>()`, `IExchangeRateService`, `IConversionService` to the same registration block as existing application services

### Foundational tests

- [ ] T050 [P] Add `tests/FundingPlatform.Tests.Unit/ExchangeRateTests.cs` covering: positive-buy/sell invariants, distinct-pair invariant, future-dated rejection, `MarkUsed` idempotency, `ConvertUsdToCrc` rounding cases (0.005 rounds away from zero, 0.004 stays)
- [ ] T051 [P] Add `tests/FundingPlatform.Tests.Unit/ConversionServiceTests.cs` covering: missing-rate throws `MissingRateException`, picks the latest by `EffectiveAtUtc`, returns snapshot consistent with chosen rate (use an in-memory test double for the rate query)
- [ ] T052 [P] Add `tests/FundingPlatform.Tests.Integration/ExchangeRateRepositoryTests.cs` (real SQL via `AspireFixture`): unique-index conflict surfaces as the FR-007 validation error; latest-rate query returns most recent
- [ ] T053 [P] Add `tests/FundingPlatform.Tests.Integration/MigrationTests.cs`: deploy dacpac to a clean DB; insert pre-existing CRC and "non-CRC without snapshot" SupplierQuote rows via raw SQL before running the post-deploy block (or seed both before and re-run); assert CRC rows are stamped (Original=Converted, Legacy=0) and non-CRC rows are flagged Legacy=1 with `ConvertedCrcAmount` NULL

**Checkpoint**: Foundation ready — schema deployed, conversion seam wired, audit-event constants live. User stories can now begin in parallel.

---

## Phase 3: User Story 1 — Applicant creates a USD supplier quote with deterministic CRC conversion (Priority: P1) 🎯 MVP

**Goal**: An applicant can pick USD on the supplier-quote form, see the CRC preview update in real time, save, and have the rate snapshot persisted on the quote line.

**Independent Test**: With one published CRC↔USD rate present, the applicant types `1000` USD on the form, sees `₡520,000.00` (assuming Buy=520) update under the input, saves, and the quote-detail page shows both `1000 USD` and `₡520,000.00 CRC` plus the rate-snapshot read-only box.

### Tests for User Story 1

- [ ] T100 [P] [US1] Add `tests/FundingPlatform.Tests.Unit/SupplierQuoteTests.cs` covering: `SetCurrencyAndAmount` snapshots a USD rate, `EditAmount` re-applies the snapshot, attempting to change currency on an existing quote throws (per FR-017a)
- [ ] T101 [P] [US1] Add `tests/FundingPlatform.Tests.Integration/SupplierQuoteCreateUsdTests.cs` against AspireFixture: creates a USD quote via the application layer, asserts row in DB has Original=1000, Converted=520000.00, Snapshot fields populated, `ExchangeRates.IsUsed = 1`
- [ ] T102 [P] [US1] Add `tests/FundingPlatform.Tests.E2E/ApplicantUsdQuoteE2E.cs` Playwright class — page-object for the quote form (`QuoteFormPage`), drives the golden path and the "no rate published → blocking validation" failure path (FR-018)

### Implementation for User Story 1

- [ ] T110 [US1] Add `src/FundingPlatform.Web/Controllers/SupplierQuotesController.cs` `Convert` action: `[HttpPost][Authorize][ValidateAntiForgeryToken] public async Task<IActionResult> Convert(ConvertRequest req)` returning the JSON shape from `contracts/conversion-preview-api.md`. Maps `MissingRateException` → 409 with the FR-018 message. Maps unknown/disabled currency → 404/400 per contract
- [ ] T111 [US1] Modify `SupplierQuotesController` create/edit actions to call `SupplierQuote.SetCurrencyAndAmount(...)` (which uses `IConversionService`) at save, persisting the snapshot. Maps `MissingRateException` to a model-state validation error so the form re-renders inline with the FR-018 message
- [ ] T112 [P] [US1] Add `src/FundingPlatform.Web/wwwroot/js/quote-conversion-preview.js` — debounced (300 ms) blur handler that POSTs to `/SupplierQuotes/Convert`, updates the preview region with `convertedCrc` + rate metadata, clears the preview when CRC is selected
- [ ] T113 [US1] Modify `src/FundingPlatform.Web/Views/SupplierQuotes/_QuoteForm.cshtml` (or whatever the existing partial is named) to: (a) replace any free-text currency input with a `<select>` populated from `ICurrencyConfigService.ListAsync()` filtered to `IsEnabled=true` and defaulting to CRC, (b) add the preview region (initially hidden), (c) reference `quote-conversion-preview.js`
- [ ] T114 [US1] Modify `src/FundingPlatform.Web/Views/SupplierQuotes/Details.cshtml` to render the rate-snapshot read-only box for non-CRC quotes (rate value, type, effective date)
- [ ] T115 [US1] Run `tests/FundingPlatform.Tests.E2E/ApplicantUsdQuoteE2E.cs` against AppHost; ensure both scenarios pass

**Checkpoint**: User Story 1 fully functional and demonstrable. The MVP slice is complete (with one rate seeded directly into the DB or via raw admin UI before US3 ships).

---

## Phase 4: User Story 2 — Applicant creates a CRC supplier quote (Priority: P1)

**Goal**: Existing CRC-only flow continues to work — currency selector defaults to CRC, no preview area appears, no snapshot persisted.

**Independent Test**: An applicant picks CRC on the form, enters `750000`, saves, and the quote detail shows `₡750,000.00 CRC` only with no conversion-applied indicator anywhere downstream.

### Tests for User Story 2

- [ ] T200 [P] [US2] Add `tests/FundingPlatform.Tests.Integration/SupplierQuoteCreateCrcTests.cs`: creates a CRC quote via the application layer, asserts Original=Converted, Snapshot fields NULL, `LegacyNeedsReview = 0`
- [ ] T201 [P] [US2] Add `tests/FundingPlatform.Tests.E2E/ApplicantCrcQuoteE2E.cs` Playwright class: golden path; assert preview region remains hidden; assert quote detail/list/dashboard show no conversion indicator

### Implementation for User Story 2

- [ ] T210 [US2] Modify `SupplierQuote.SetCurrencyAndAmount` to short-circuit when `currency == Crc`: assign Original=Converted=amount, Snapshot=null, LegacyNeedsReview=false. Add a unit test in `SupplierQuoteTests.cs` for this branch
- [ ] T211 [US2] Modify `quote-conversion-preview.js` to hide/clear the preview region when the selector switches to CRC (already covered by T112 if implemented carefully; this task verifies and adds an E2E selector hook)
- [ ] T212 [US2] Run `ApplicantCrcQuoteE2E.cs` and confirm pass

**Checkpoint**: US1 + US2 both green. Applicant flow end-to-end stable.

---

## Phase 5: User Story 3 — Administrator manages enabled currencies and exchange rates (Priority: P1)

**Goal**: An administrator can toggle USD enable/disable (CRC permanently locked) and publish CRC↔USD reference rates with buy + sell + effective timestamp. Rates are immutable once used.

**Independent Test**: The admin loads `/Admin/Currencies`, disables USD, re-enables it; loads `/Admin/ExchangeRates`, publishes a new rate, sees it become active; attempts to edit/delete a used rate and sees the FR-008 rejection.

### Tests for User Story 3

- [ ] T300 [P] [US3] Add `tests/FundingPlatform.Tests.Integration/CurrencyConfigServiceTests.cs`: enable/disable USD, attempt to disable CRC throws, audit-log events are written
- [ ] T301 [P] [US3] Add `tests/FundingPlatform.Tests.Integration/ExchangeRateServiceTests.cs`: create accepts valid input, rejects zero/negative, rejects future-dated (FR-007a), rejects duplicate (FR-007); edit/delete via repository surface attempts both succeed at "first try" then fail once `IsUsed=1`; audit events on every create + every blocked attempt
- [ ] T302 [P] [US3] Add `tests/FundingPlatform.Tests.E2E/AdminCurrencyConfigE2E.cs` (admin currency page-object, scenarios: toggle USD, attempt-disable CRC error message)
- [ ] T303 [P] [US3] Add `tests/FundingPlatform.Tests.E2E/AdminExchangeRateE2E.cs` (rate page-object, scenarios: create valid rate, validate zero-buy/zero-sell/duplicate-timestamp/future-dated rejections, view history list, attempt-edit-used-rate rejection)

### Implementation for User Story 3

- [ ] T310 [P] [US3] Implement `src/FundingPlatform.Application/Currencies/CurrencyConfigService.cs`: enforces CRC-permanent invariant, writes audit events
- [ ] T311 [P] [US3] Implement `src/FundingPlatform.Application/ExchangeRates/ExchangeRateService.cs`: validates per FR-006/FR-007/FR-007a, catches `DbUpdateException` with SQL error 2627/2601 → maps to FR-007 message, writes audit events on every create + every blocked attempt
- [ ] T312 [US3] Add `src/FundingPlatform.Web/Areas/Admin/Controllers/CurrenciesController.cs` exposing the endpoints in `contracts/currency-api.md`
- [ ] T313 [US3] Add `src/FundingPlatform.Web/Areas/Admin/Controllers/ExchangeRatesController.cs` exposing the endpoints in `contracts/exchange-rate-api.md`. PUT/DELETE return `405 Method Not Allowed` and write `ExchangeRate.EditAttemptBlocked` / `DeleteAttemptBlocked` audit events
- [ ] T314 [P] [US3] Add `src/FundingPlatform.Web/Areas/Admin/Views/Currencies/Index.cshtml` (Tabler-styled list with enable/disable toggles)
- [ ] T315 [P] [US3] Add `src/FundingPlatform.Web/Areas/Admin/Views/ExchangeRates/Index.cshtml` (history list with active-rate highlight)
- [ ] T316 [P] [US3] Add `src/FundingPlatform.Web/Areas/Admin/Views/ExchangeRates/Create.cshtml` (form, client-side validation matches server rules)
- [ ] T317 [US3] Wire navigation links into the existing admin sidebar/menu (consistent with how spec 009 added other admin pages)
- [ ] T318 [US3] Run all four US3 tests against AppHost; ensure pass

**Checkpoint**: US1, US2, US3 all green. The applicant flow now works end-to-end without dev-only DB seeding because admins can publish rates through the UI.

---

## Phase 6: User Story 4 — Reviewers, approvers, and dashboards display multi-currency clearly (Priority: P2)

**Goal**: Every quote/total surface in the platform shows original + converted CRC and a conversion indicator with rate tooltip. Cross-currency request totals roll up in CRC.

**Independent Test**: A reviewer opens a request with one CRC and one USD quote and confirms each line shows original + CRC + tooltip; the request total equals the CRC sum; CRC-only lines have no tooltip.

### Tests for User Story 4

- [ ] T400 [P] [US4] Add `tests/FundingPlatform.Tests.Integration/RequestTotalRollupTests.cs`: build a request with mixed CRC+USD quotes, query the projection, assert total = sum of converted-CRC, legacy-flagged quotes excluded
- [ ] T401 [P] [US4] Add `tests/FundingPlatform.Tests.E2E/ReviewerDisplayE2E.cs`: reviewer logs in, opens mixed request, asserts both lines + tooltip + total

### Implementation for User Story 4

- [ ] T410 [P] [US4] Add `src/FundingPlatform.Web/ViewComponents/MoneyDisplayViewComponent.cs` + `Default.cshtml`: takes `(decimal? original, CurrencyCode? originalCurrency, decimal? convertedCrc, ExchangeRateSnapshot? snapshot)`, formats per FR-021. CRC-only renders the CRC string; non-CRC renders `$1,000.00 USD` + `(₡520,000.00 CRC)` + indicator
- [ ] T411 [P] [US4] Add `src/FundingPlatform.Web/ViewComponents/ConversionIndicatorViewComponent.cs` + `Default.cshtml`: small ⓘ icon with `data-bs-toggle="tooltip"` carrying rate value + type + effective date
- [ ] T412 [US4] Update the supplier-quote list view (`Views/SupplierQuotes/Index.cshtml` or the request-summary partial that lists quotes) to render every monetary cell via `MoneyDisplayViewComponent`
- [ ] T413 [US4] Update the request-summary view to compute the request total as the sum of `ConvertedCrcAmount` (excluding `LegacyNeedsReview = 1`) and display via `MoneyDisplayViewComponent` with `originalCurrency = null` so it renders pure CRC
- [ ] T414 [US4] Update applicant dashboard projection (`IApplicantDashboardProjection` per CLAUDE.md) and reviewer dashboard projections to include both original + converted CRC fields, then update the corresponding views to render via the view components
- [ ] T415 [US4] Update approval screen (existing controller/view from spec 002 review-approval-workflow) to render via `MoneyDisplayViewComponent`
- [ ] T416 [US4] Update admin reports (CSV streaming endpoint from spec 010) to emit both `OriginalCurrencyCode`, `OriginalAmount`, and `ConvertedCrcAmount` columns per FR-021. Existing CSV row-limit (`AdminReports:CsvRowLimit`) is unchanged
- [ ] T417 [US4] Run `ReviewerDisplayE2E` and `RequestTotalRollupTests`; ensure pass

**Checkpoint**: US1–US4 green. Multi-currency now consistent across the entire authenticated UI surface.

---

## Phase 7: User Story 5 — Final agreement PDF shows CRC with conversion indicator (Priority: P2)

**Goal**: The PDF renders CRC only with a per-line conversion note when any quote line came from non-CRC; CRC-only requests render unchanged. PDF refuses on missing snapshot.

**Independent Test**: Generate a PDF for a mixed request — verify CRC-only display + conversion notes; generate one for a CRC-only request — verify visual identity to today's output; corrupt a snapshot to NULL and verify FR-027 inline error + log entry.

### Tests for User Story 5

- [ ] T500 [P] [US5] Add `tests/FundingPlatform.Tests.Integration/PdfRenderingTests.cs`: golden test for CRC-only PDF (compare extracted text/structure to a baseline checked into `tests/Fixtures/pdfs/`), mixed-currency PDF includes the per-line conversion note with rate value + Buy + effective date, missing-snapshot row triggers domain exception
- [ ] T501 [P] [US5] Add `tests/FundingPlatform.Tests.E2E/AgreementPdfMultiCurrencyE2E.cs`: drives the agreement page, downloads PDF, asserts file is non-empty + magic bytes; the inline error path asserts the user-visible message and that the application log contains the offending quote id

### Implementation for User Story 5

- [ ] T510 [P] [US5] Modify `src/FundingPlatform.Web/Views/Agreements/_QuoteLine.cshtml` (or whatever the existing PDF Razor partial is) to render the conversion note row beneath each non-CRC line: `Conversion: 1 USD = ₡<rate> (Buy, effective <date>)`. Locale-aware formatting via existing `CultureInfo` helpers (es-CR primary)
- [ ] T511 [US5] Modify `src/FundingPlatform.Infrastructure/Pdf/AgreementPdfRenderer.cs`: throw `MissingConversionMetadataException` (new domain exception in `src/FundingPlatform.Domain/Exceptions/`) when any quote line in the request has `OriginalCurrencyCode != 'CRC'` AND `Snapshot is null`. Renderer otherwise feeds the partial with the new view model fields
- [ ] T512 [US5] Modify `src/FundingPlatform.Web/Controllers/AgreementsController.cs` Pdf action to catch `MissingConversionMetadataException` and: (a) write a structured log entry containing the offending quote ids; (b) re-render the request page with a `TempData` error message visible to the user attempting the action (FR-027). Other exceptions continue to propagate
- [ ] T513 [US5] Add a baseline PDF artifact under `tests/Fixtures/pdfs/crc-only-baseline.pdf` (committed) and a `tests/Fixtures/pdfs/mixed-baseline.expected.txt` extraction for the PdfRenderingTests golden assertions
- [ ] T514 [US5] Run all US5 tests; ensure pass

**Checkpoint**: US1–US5 green. The legally-meaningful PDF now reflects multi-currency reality.

---

## Phase 8: User Story 6 — Legacy USD quotes flagged and quarantined (Priority: P3)

**Goal**: Pre-existing CRC quotes auto-stamp; pre-existing non-CRC quotes lacking conversion metadata are flagged `LegacyNeedsReview = 1`, excluded from totals, and admin can attach a historical rate to clear the flag.

**Independent Test**: Insert synthetic legacy quotes pre-deploy → re-deploy → confirm CRC rows are stamped, non-CRC are flagged. Open `/Admin/LegacyQuotes`, attach a rate to one, confirm flag clears and row appears in cross-currency totals.

### Tests for User Story 6

- [ ] T600 [P] [US6] Extend `tests/FundingPlatform.Tests.Integration/MigrationTests.cs` (built in T053) with re-run idempotency assertion (running the post-deploy block twice does not re-flag already-attached quotes)
- [ ] T601 [P] [US6] Add `tests/FundingPlatform.Tests.Integration/LegacyQuoteRateAttachServiceTests.cs`: attaches a historical rate; asserts snapshot fields set, ConvertedCrcAmount computed, flag cleared, audit `SupplierQuote.LegacyRateAttached` event written
- [ ] T602 [P] [US6] Add `tests/FundingPlatform.Tests.E2E/LegacyQuoteFlowE2E.cs`: log in as admin, navigate to legacy queue, attach rate, verify quote now appears with conversion data in the request and the flag is gone

### Implementation for User Story 6

- [ ] T610 [US6] Add `src/FundingPlatform.Application/SupplierQuotes/LegacyQuoteRateAttachService.cs` implementing the attach use case (reads the user-selected `ExchangeRate`, calls `SupplierQuote.AttachLegacyRate(...)`, writes the audit event, persists)
- [ ] T611 [US6] Add `src/FundingPlatform.Web/Areas/Admin/Controllers/LegacyQuotesController.cs` with `Index` (list flagged) and `POST Attach` (rate-id + quote-id binding)
- [ ] T612 [US6] Add `src/FundingPlatform.Web/Areas/Admin/Views/LegacyQuotes/Index.cshtml`: filtered list, per-row "attach rate" form using the existing rate-history list as the rate picker
- [ ] T613 [US6] Wire admin nav link
- [ ] T614 [US6] Run all US6 tests; ensure pass

**Checkpoint**: All six user stories green. End-to-end story coverage complete.

---

## Phase 9: Polish & Cross-Cutting Concerns

- [ ] T900 [P] Add es-CR resx strings for new admin/applicant copy (currency-disabled labels, conversion indicator tooltip, validation messages including the literal "No reference exchange rate is configured. Contact an administrator." per spec FR-018)
- [ ] T901 [P] Verify the Tabler styling is consistent with existing admin pages (no new CDN refs; vendor any new assets locally per CLAUDE.md "No CDN" rule)
- [ ] T902 Walk through `quickstart.md` end-to-end against a fresh AppHost run; fix any drift between docs and code
- [ ] T903 Run the full E2E suite from a clean checkout: `dotnet test tests/FundingPlatform.Tests.E2E`. ALL tests must be green per Constitution III + project memory ("Delivery requires a personally-executed green E2E run")
- [ ] T904 Update `CLAUDE.md` "Active Technologies" / "Recent Changes" sections to mention spec 015 (multi-currency)
- [ ] T905 Final commit + push for the feature branch

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
Task: "Add tests/FundingPlatform.Tests.Unit/SupplierQuoteTests.cs covering currency + edit semantics (T100)"
Task: "Add tests/FundingPlatform.Tests.Integration/SupplierQuoteCreateUsdTests.cs (T101)"
Task: "Add tests/FundingPlatform.Tests.E2E/ApplicantUsdQuoteE2E.cs (T102)"

# Frontend asset and JS in parallel with controller wiring:
Task: "Add quote-conversion-preview.js (T112)"
Task: "Modify SupplierQuotesController.Convert action (T110)"
```

---

## Implementation Strategy

### MVP First (User Story 1 only)

1. Complete Phase 1 + Phase 2.
2. Complete Phase 3 (US1).
3. STOP — demo USD quote with admin-seeded rate (DB-direct).

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
