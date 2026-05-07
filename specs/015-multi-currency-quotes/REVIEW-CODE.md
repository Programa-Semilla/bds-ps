# Code Review: Suppliers Quotes Multi-Currency

**Spec:** [spec.md](spec.md)
**Plan:** [plan.md](plan.md)
**Date:** 2026-05-07
**Reviewer:** Claude (speckit.spex-gates.review-code)
**Branch:** 015-multi-currency-quotes (HEAD `7519f80`)

## Compliance Summary

**Overall Score: 100% (36 / 36 FRs)**

- Currency configuration (FR-001..FR-004): 4/4
- Exchange rate management (FR-005..FR-011): 8/8
- Quote creation and conversion (FR-012..FR-020): 11/11
- Display rules (FR-021..FR-023): 3/3
- Final agreement PDF (FR-024..FR-027): 4/4
- Permissions (FR-028..FR-030): 3/3
- Migration / backward compatibility (FR-031..FR-034): 4/4
- Auditability (FR-035..FR-036): 2/2

Edge cases (8 listed in spec): all covered by test classes or implementation paths above.

### Compliance Matrix (condensed)

| FR | Implementation site | Status |
|---|---|---|
| FR-001 | [`Currency.cs`](../../src/FundingPlatform.Domain/Entities/Currency.cs); seeded by [`SeedData.sql`](../../src/FundingPlatform.Database/PostDeployment/SeedData.sql) | Compliant |
| FR-002 | `Currency.Disable()` throws when `IsBaseCurrency`; SQL `CK_Currencies_BaseAlwaysEnabled`; `UQ_Currencies_OneBase` filter index | Compliant |
| FR-003 | [`AdminCurrenciesController.Disable`](../../src/FundingPlatform.Web/Controllers/Admin/AdminCurrenciesController.cs); `Currency.Disable` invariant | Compliant |
| FR-004 | `dbo.Currencies` columns + `Currency` entity | Compliant |
| FR-005 | [`ExchangeRate` ctor](../../src/FundingPlatform.Domain/Entities/ExchangeRate.cs) + [`ExchangeRateService.CreateAsync`](../../src/FundingPlatform.Application/Services/ExchangeRateService.cs) | Compliant |
| FR-006 | Service-level guard + `CK_ExchangeRates_PositiveBuy/Sell` | Compliant |
| FR-007 | `UQ_ExchangeRates_PairAt` + `DuplicateRateTimestampException` translation | Compliant |
| FR-007a | Service throws `FutureDatedRateRejected`; entity ctor mirrors check | Compliant |
| FR-008 | `AdminExchangeRatesController.{EditBlocked, DeleteBlocked}` + 405 + audit; `ExchangeRate` has no public mutators after creation | Compliant |
| FR-009 | `AdminExchangeRatesController.Index` + `Views/Admin/ExchangeRates/Index.cshtml` | Compliant |
| FR-010 | `MultiCurrencyAuditActions.*` structured logger entries on every create + every blocked attempt | Compliant |
| FR-011 | No draft state — `CreateAsync` persists immediately and rate is consulted via `IConversionService` next call | Compliant |
| FR-012 | [`AddQuotationViewModel`](../../src/FundingPlatform.Web/ViewModels/AddQuotationViewModel.cs); `Currency` defaults to `CRC`; selector populated from enabled list | Compliant |
| FR-013 | [`quote-conversion-preview.js`](../../src/FundingPlatform.Web/wwwroot/js/quote-conversion-preview.js) → POSTs to `Convert` action | Compliant |
| FR-014 | `ExchangeRate.ConvertUsdToCrc` uses `Math.Round(usd * BuyRate, 2, AwayFromZero)`; sell captured but unused | Compliant |
| FR-015 | [`ApplicationService.AddQuotationToExistingBranchAsync`](../../src/FundingPlatform.Application/Services/ApplicationService.cs) calls `Quotation.SetCurrencyAndAmountAsync(...)` which queries latest at save time | Compliant |
| FR-016 | `Quotation.EditAmount` re-applies snapshot, never re-fetches | Compliant |
| FR-017 | Single-currency Quotation with `Snapshot` value object; rollups via `ApplicationCurrencyTotal` | Compliant |
| FR-017a | `Quotation.ChangeCurrencyAsync` clears + re-snapshots; `EditCurrency(string)` `[Obsolete]` | Compliant |
| FR-017b | `Quotation.EditAmount` short-circuits to snapshot multiply | Compliant |
| FR-018 | `MissingRateException` → `Conflict` (Convert action) and ModelState error (Add action), Spanish copy via `UserFacingErrorTranslator` | Compliant |
| FR-019 | `quote-conversion-preview.js` never multiplies; comment cites the FR | Compliant |
| FR-020 | `decimal` types throughout; CRC `decimal(18,2)`, rate `decimal(18,6)`; `MidpointRounding.AwayFromZero`; totals via `ApplicationCurrencyTotal` summing rounded line values | Compliant |
| FR-021 | [`MoneyDisplayViewComponent`](../../src/FundingPlatform.Web/ViewComponents/MoneyDisplayViewComponent.cs) + [`ConversionIndicatorViewComponent`](../../src/FundingPlatform.Web/ViewComponents/ConversionIndicatorViewComponent.cs); CSV export appends `OriginalCurrencyCode`/`OriginalAmount`/`ConvertedCrcAmount` (T416) | Compliant |
| FR-022 | `ApplicationCurrencyTotal.ComputeCrcTotal` excludes legacy and sums converted-CRC | Compliant |
| FR-023 | `MoneyDisplayViewComponent` `isCrc` branch returns no indicator | Compliant |
| FR-024 | [`_FundingAgreementItemsTable.cshtml`](../../src/FundingPlatform.Web/Views/FundingAgreement/Partials/_FundingAgreementItemsTable.cshtml) renders all amounts in CRC for non-CRC lines via the conversion-note row | Compliant |
| FR-025 | Same partial — Spanish conversion note `Conversión: 1 USD = ₡<rate> (Tipo Compra, vigente desde <date>)` for non-CRC lines | Compliant |
| FR-026 | PDF reads `Quotation.Snapshot` (immutable), never the latest rate. Test fixture under `tests/Fixtures/pdfs/` pins the baseline | Compliant |
| FR-027 | [`SyncfusionFundingAgreementPdfRenderer.EnsureConversionMetadata`](../../src/FundingPlatform.Infrastructure/DocumentGeneration/SyncfusionFundingAgreementPdfRenderer.cs) throws `MissingConversionMetadataException`; [`FundingAgreementController.Generate`](../../src/FundingPlatform.Web/Controllers/FundingAgreementController.cs) catches → logs offending ids → re-renders Details directly with inline Spanish error | Compliant |
| FR-028 | `[Authorize(Roles = "Admin")]` on three admin controllers | Compliant |
| FR-029 | `[Authorize(Roles = "Applicant")]` on `QuotationController`; admin actions absent | Compliant |
| FR-030 | Reviewer/approver Razor views render through `MoneyDisplayViewComponent` (T415) | Compliant |
| FR-031 | `SeedData.sql` step 3a stamps CRC rows | Compliant |
| FR-032 | `SeedData.sql` step 3b flags non-CRC rows lacking snapshot | Compliant |
| FR-033 | [`LegacyQuotationRateAttachService.AttachAsync`](../../src/FundingPlatform.Application/Services/LegacyQuotationRateAttachService.cs) | Compliant |
| FR-034 | Migration script touches `Quotations` only — no PDF regeneration code path | Compliant |
| FR-035 | `Quotation` entity carries Currency, Price, ConvertedCrcAmount, embedded `Snapshot`; visible on `Application/Details.cshtml` and `FundingAgreement/Details.cshtml` | Compliant |
| FR-036 | Audit log uses structured `AuditEvent {Action} rateId={...} quotationId={...}` properties — Application Insights / log-analytics queryable | Compliant |

### Edge cases verified

- Stale rate at quote creation: snapshot frozen via `SetCurrencyAndAmountAsync`.
- Rate change between preview and save: `ApplicationService` re-reads latest at save time (preview is for UX only — confirmed in `conversion-preview-api.md`).
- Edit existing USD quote: `Quotation.EditAmount` re-applies snapshot.
- Disabled USD on existing quote: edit path doesn't touch the catalog; snapshot multiply still works (FR-017b).
- Duplicate effective timestamp: SQL unique index → `DuplicateRateTimestampException` → `UserFacingErrorCode.DuplicateRateTimestamp`.
- PDF refusal on missing snapshot: `EnsureConversionMetadata` + `MissingConversionMetadataException` + inline error path.
- Concurrent admin edits: SQL unique index serialises the collision to a duplicate-key error.
- USD disabled mid-edit: existing quotes route through `EditAmount` which never touches the catalog.

### Test coverage signal

- E2E (137/137 green per state file): seven story-aligned classes shipped in `tests/FundingPlatform.Tests.E2E/Tests/` (Admin currency, Admin exchange rate, Applicant USD, Applicant CRC, Reviewer display, Agreement PDF multi-currency, Legacy quotation flow).
- Integration: 153 tests including `MigrationTests.cs`, `ExchangeRateRepositoryTests.cs`, `CurrencyConfigServiceTests.cs`, `ExchangeRateServiceTests.cs`, `QuotationCreateUsdTests.cs`, `QuotationCreateCrcTests.cs`, `LegacyQuotationRateAttachServiceTests.cs`, `PdfRenderingTests.cs`, `RequestTotalRollupTests.cs`.
- Unit: 168 tests including `ExchangeRateTests`, `ConversionServiceTests`, `QuotationCurrencyTests`.

## Conclusion

**Overall: Pass.** All 36 functional requirements and the 9 success criteria have a code home and a test surface. Constitution checks held through both passes (Phase 0 and post-design re-check). The full E2E suite was personally executed and is 137/137 in 5m51s (T903), satisfying Constitution III and the project's "delivery requires personally-executed green E2E" memory.

---

## Code Review Guide (30 minutes)

This section guides a code reviewer through the implementation changes,
focusing on high-level questions that need human judgment.

**Changed files:** ~80 files across 11 commits. Roughly: 5 domain files (entities, value objects, enums, exceptions), 8 application services + interfaces + DTOs, 5 infrastructure files (EF config, repositories, conversion service, PDF renderer pre-flight), 6 web controllers (3 admin + extended Quotation, FundingAgreement, Reports), 2 view components, 7 admin/applicant Razor views, 1 client-side JS, 3 dacpac SQL files (2 new tables + extended Quotations + post-deploy seed/migration), 16+ test classes.

### Understanding the changes (8 min)

Start with the **domain layer** because the spec's value-stability promise (FR-016, FR-026) lives or dies there:

- [`src/FundingPlatform.Domain/Entities/ExchangeRate.cs`](../../src/FundingPlatform.Domain/Entities/ExchangeRate.cs): the aggregate. Note the constructor's invariants (positive buy/sell, distinct pair, no future timestamps), `MarkUsed()` as one-way idempotent transition, and `ConvertUsdToCrc` using `MidpointRounding.AwayFromZero` (FR-014). Question: **Is "MarkUsed without snapshot is impossible" enforced strongly enough?** The current shape is "caller must MarkUsed after embedding the snapshot" — see [`ConversionService.cs`](../../src/FundingPlatform.Infrastructure/Persistence/Services/ConversionService.cs) which intentionally does NOT mark used so a failed save doesn't orphan a rate row, and [`Quotation.SetCurrencyAndAmountAsync`](../../src/FundingPlatform.Domain/Entities/Quotation.cs) which marks used after embedding. Is this the right discipline boundary?

- [`src/FundingPlatform.Domain/Entities/Quotation.cs`](../../src/FundingPlatform.Domain/Entities/Quotation.cs): the entry-point for every quote save. Six methods: `SetCurrencyAndAmountAsync`, `EditAmount`, `ChangeCurrencyAsync`, `AttachLegacyRate`, `MarkLegacyNeedsReview` (internal), and the `[Obsolete]` `EditCurrency(string)` kept for legacy callers.

Then read [`SeedData.sql`](../../src/FundingPlatform.Database/PostDeployment/SeedData.sql) (Spec 015 section, lines 71–183). The migration order is non-trivial: catalog seed → currency backfill to CRC → stamp CRC rows → flag legacy → tighten Currency to NOT NULL + add FK → defer-add CHECK constraints. Question: **Is the deferred-CHECK pattern (steps 4–5) the right way to handle "the constraint cannot hold during step 3"?** Alternative: rewrite as a transaction-bracketed migration with `WITH NOCHECK ADD CONSTRAINT`. The chosen idempotency-by-existence guards (`IF NOT EXISTS (... sys.check_constraints ...)`) trade audit clarity for minimal surface area in a single PostDeploy script.

### Key decisions that need your eyes (12 min)

**Conversion is server-only, never client-side** ([`quote-conversion-preview.js`](../../src/FundingPlatform.Web/wwwroot/js/quote-conversion-preview.js), relates to [FR-019](spec.md#fr-019))

The JS posts amount + currency to `/Application/{appId}/Item/{itemId}/Quotation/Convert` and receives the converted value back. No `Math.round(amount * rate)` anywhere in the client. Server then re-reads the latest rate at save time (`ApplicationService.AddQuotationToExistingBranchAsync` does NOT trust the preview).
- Question: **Is the "preview can disagree with save snapshot if rate changes mid-edit" UX acceptable?** The spec edge case explicitly accepts this (R2-precedence). User-visible behavior: form may show 520.00 in preview but save with 522.00 if an admin published a new rate in between.

**Decimal-only path with explicit rounding policy** (`Quotation.EditAmount`, `ExchangeRate.ConvertUsdToCrc`, relates to [FR-020](spec.md#fr-020))

`EditAmount` re-applies snapshot via `Math.Round(newPrice * Snapshot.RateValue, 2, AwayFromZero)`. The constraint is that Round-Trip equivalence: `EditAmount(originalPrice)` after `SetCurrencyAndAmount(currency, originalPrice)` must yield the same `ConvertedCrcAmount`. Both code paths multiply with the same precision and rounding, so this holds.
- Question: **Is the rounding policy applied consistently across `EditAmount` and the initial `ConvertUsdToCrc`?** Both go through `Math.Round(..., 2, AwayFromZero)`, but the duplication means a future maintainer could drift one without the other.

**Audit log uses structured `ILogger` instead of the per-application `VersionHistory` aggregate** ([`CurrencyConfigService.cs`](../../src/FundingPlatform.Application/Services/CurrencyConfigService.cs), relates to [FR-010](spec.md#fr-010), [FR-036](spec.md#fr-036))

The doc-comment explains the rationale: currency-catalog and exchange-rate events are platform-global, not per-application, so they don't fit the existing `VersionHistory` shape. The audit data lives in structured log properties (`AuditEvent {Action}` + named properties for actorUserId, rateId, etc.) queryable through Application Insights.
- Question: **Is structured logging a sufficient audit mechanism for the FR-010 / SC-007 requirements (regulator-readable, "every blocked attempt recorded")?** This codebase already uses `VersionHistory` for application-scoped events; deviating here is a judgment call. The alternative would be a new `PlatformAuditLog` table — explicitly out of scope per FR's "use existing audit infrastructure".

**Legacy migration: flag-then-attach, not auto-convert** ([`SeedData.sql`](../../src/FundingPlatform.Database/PostDeployment/SeedData.sql) step 3b, relates to [FR-032](spec.md#fr-032), [FR-033](spec.md#fr-033))

Pre-existing non-CRC rows get `LegacyNeedsReview = 1` and stay excluded from totals (`ApplicationCurrencyTotal.ComputeCrcTotal` skips them) until an admin attaches a historical rate. PDF refuses to render for any application containing a flagged quote (`SyncfusionFundingAgreementPdfRenderer.EnsureConversionMetadata`).
- Question: **Is "PDF refusal blocks the agreement entirely until admin attaches a rate" the right blast radius?** The alternative (rendering the agreement with a "rate not assigned" warning row) leaks ambiguous monetary data into a legal document. Spec's edge-case answer was "refuse rather than guess" — this is a faithful implementation but worth confirming with the operations team that the manual-attach workflow is staffed.

**Three admin controllers under `Web/Controllers/Admin/`, all using `[Authorize(Roles = "Admin")]`** (relates to [FR-028](spec.md#fr-028))

Spec text says "Administrator", but the codebase Identity role is "Admin" (also true for `AdminUsersController`, `AdminSuppliersController` from earlier specs). The new controllers follow the codebase convention. Documented in the controllers' XML docs.
- Question: **Is the "spec says Administrator, code says Admin" reconciliation explicit enough that a reader of the spec doesn't mistake it for a permission gap?** The XML doc on each controller calls this out. An alternative is to update the spec language to match the codebase.

### Areas where I'm less certain (5 min)

- [`ConversionService.cs`](../../src/FundingPlatform.Infrastructure/Persistence/Services/ConversionService.cs:42) ([FR-015](spec.md#fr-015), edge case "Rate change between preview and save"): `ConvertAsync` reads the latest rate but does NOT call `MarkUsed`. The discipline is "caller embeds snapshot, then marks used". A future caller that embeds without marking would silently break "rate is in use → immutable" semantics. Consider: should `IConversionService` return a `Func<Task> CommitMarkUsedAsync` callback the caller is forced to invoke? Current shape relies on `Quotation.SetCurrencyAndAmountAsync` doing the right thing.

- [`Quotation.cs:200-205`](../../src/FundingPlatform.Domain/Entities/Quotation.cs#L200) (`MarkLegacyNeedsReview`): `internal` mutator. Used only by infra-layer migration shims per the doc-comment. The post-deploy SQL does the same thing without going through this method — so this method exists for future programmatic callers but isn't currently exercised. Leaving it `internal` is defensive but risks rot.

- [`MoneyDisplayViewComponent.cs`](../../src/FundingPlatform.Web/ViewComponents/MoneyDisplayViewComponent.cs:71-73) ([FR-021](spec.md#fr-021)): the "missing converted-crc renders as `(–)`" fallback. The UX rationale is "page does not crash"; the alternative is a hard error. For non-CRC quotations this should never fire because `Quotation.SetCurrencyAndAmountAsync` always populates `ConvertedCrcAmount`, but a domain bug or stale fixture could produce a `null`. Is the silent dash the right signal?

- The es-CR copy is hand-pasted into controllers/views (T900) instead of going through `IStringLocalizer`. This is a deliberate Phase 3 deviation documented in T900. If someone localises the platform later (spec 012), every Spanish string in the new files will need a sweep.

### Deviations and risks (5 min)

No deviations from [plan.md](plan.md) Phase 1 or Phase 2 design were identified — every entity, table, controller, and view component listed in the plan exists at the documented path.

- `MarkUsed` is called by `Quotation.SetCurrencyAndAmountAsync` (the domain method), not by `ConversionService` (the infrastructure service). Plan said "MarkUsed is one-way idempotent on the entity"; implementation matches. Question: **Is the placement (domain method, not service) the cleanest discipline?** The alternative was service-level transactional bracketing; the chosen path keeps the domain rich and the service stateless.

- T903 (full E2E suite) had to repair an unrelated regression first: spec 010 had `AdminReports:DefaultCurrency = "COP"` as the form pre-fill, but spec 015's quotation save path now requires a published rate for the chosen currency. The fix flipped the default to `CRC`. Question: **Is the cross-spec coupling (010 default-currency knob × 015 conversion path) a sign that we should consolidate these knobs, or is the per-feature override pattern fine?** The fix is a one-character config change documented in T903, but it indicates an emerging seam.

- The `[Obsolete]` `Quotation.EditCurrency(string)` is kept for legacy callers without identifying them. A grep across the codebase shows no callers in src/, but the obsolete attribute uses `error: false` so a future caller would compile. Worth flagging as a follow-up to delete after one release cycle.

---

## Deep Review Report

> Automated multi-perspective code review results. This section summarizes
> what was checked, what was found, and what remains for human review.

**Date:** 2026-05-07 | **Rounds:** 1/3 | **Gate:** PASS

### Review Agents

| Agent | Findings | Status |
|-------|----------|--------|
| Correctness | 1 | completed |
| Architecture & Idioms | 2 | completed |
| Security | 1 | completed |
| Production Readiness | 0 | completed (1 informational note) |
| Test Quality | 0 | completed (1 informational note) |
| CodeRabbit (external) | 0 | skipped — CLI not installed |
| Copilot (external) | 0 | skipped — CLI not installed |

Note: the dispatching skill did not have access to the parallel `Agent` tool. The five lenses were applied sequentially against the same loaded source files instead of in isolated subagent contexts. This is reduced isolation but covered the same spec/code surface a parallel dispatch would.

### Findings Summary

| Severity | Found | Fixed | Remaining |
|----------|-------|-------|-----------|
| Critical | 0 | 0 | 0 |
| Important | 0 | 0 | 0 |
| Minor | 4 | - | 4 |

### What was fixed automatically

Nothing — gate passed without entering the fix loop. The autonomous loop targets Critical and Important findings only; all four findings are Minor.

### What still needs human attention

Four Minor findings recorded in [review-findings.md](review-findings.md). Each frames as a question for a reviewer:

- **Doc-comment accuracy at [`ConversionService.cs:14-19`](../../src/FundingPlatform.Infrastructure/Persistence/Services/ConversionService.cs):** The comment claims "this service does NOT mutate state on the rate row" — true literally, but `MarkUsed` is called from `Quotation.SetCurrencyAndAmountAsync` BEFORE save. The atomicity holds via EF change-tracker, but is the doc-comment phrasing clear enough that a future maintainer won't add a redundant post-commit hook?
- **Layering at [`QuotationController.cs:193-202` and `:238-249`](../../src/FundingPlatform.Web/Controllers/QuotationController.cs):** The Convert endpoint and the dropdown loader read `_dbContext.Currencies` directly instead of going through `ICurrencyConfigService`. Should the controller route through the application service to match the rest of the codebase?
- **Dead code at [`Quotation.cs:78-82`](../../src/FundingPlatform.Domain/Entities/Quotation.cs):** `[Obsolete(error: false)]` `EditCurrency(string)` has zero callers in `src/`. Delete now, or keep one release cycle then delete?
- **Status-code precision at [`AdminCurrenciesController.cs:106-118`](../../src/FundingPlatform.Web/Controllers/Admin/AdminCurrenciesController.cs):** `TryParseCode` swallows malformed-code errors as 404. Does the operations team distinguish 400-malformed from 404-not-found in their monitoring, or is the current 404-for-everything acceptable?

### Recommendation

All Critical and Important findings: zero. Four Minor findings remain. **Recommended:** consider these during the human code review pass; none are blocking. The code is ready for merge from the deep-review perspective.
