# Quickstart: Suppliers Quotes Multi-Currency

A developer's guide to running the feature locally and verifying each user story end-to-end.

## 1. Boot AppHost

```bash
dotnet run --project src/FundingPlatform.AppHost
```

This starts SQL Server (Aspire-managed), deploys the dacpac (which now includes `Currencies`, `ExchangeRates`, and the extended `Quotations` columns), seeds CRC + USD, and brings up the Web app.

## 2. Sign in as Administrator

Outside ephemeral mode, the sentinel admin uses the configured `Admin:DefaultPassword`. In ephemeral E2E mode, it is `admin@FundingPlatform.com` / `Sentinel123!`.

## 3. Configure currencies (User Story 3)

Navigate to **Admin → Currencies** (`/Admin/AdminCurrencies`). You should see two rows:

| Code | Symbol | DisplayName        | IsEnabled | IsBaseCurrency |
|------|--------|--------------------|-----------|----------------|
| CRC  | ₡      | Costa Rican colón | Yes       | Yes            |
| USD  | $      | US dollar          | Yes       | No             |

Disable USD → the row updates and an audit-log entry `Currency.Disabled` is written. Re-enable to restore.

Attempting to disable CRC returns a `409 Conflict` with the FR-002 message.

## 4. Publish the first exchange rate (User Story 3)

Navigate to **Admin → Exchange Rates → New** (`/Admin/AdminExchangeRates/Create`). Enter:

- Source: `USD`
- Target: `CRC`
- Buy rate: `520.000000`
- Sell rate: `525.000000`
- Effective: now

Save. The rate appears in the history list with `IsUsed = false`. Audit-log entry `ExchangeRate.Created`.

Try to save a duplicate timestamp → `409 Conflict` with FR-007 message.
Try `buy = 0` → `400 Bad Request` with FR-006 message.
Try a future-dated effective timestamp → `400 Bad Request` with FR-007a message.

## 5. Create a USD supplier quotation (User Story 1)

Sign in as an applicant with an active Application that has at least one Item. Open the Item → **Add Quotation** (the existing route `/Application/{appId}/Item/{itemId}/Quotation/Add`).

- Currency: `USD`
- Amount: `1000.00`

The form calls `POST /Application/{appId}/Item/{itemId}/Quotation/Convert` and displays:

```
Conversión a colones
₡520,000.00
1 USD = 520 CRC (Tipo Compra, vigente desde 2026-05-06 14:00)
```

Save the quote. Reload the quote detail — original, converted, rate snapshot, and the source rate id are all persisted. The `ExchangeRates` row's `IsUsed` is now `true`.

## 6. Verify rate-change isolation

As Administrator, publish a new rate (e.g., `Buy = 600`). Reload the USD quote you just saved. The displayed converted CRC amount and rate snapshot are **unchanged** (still `520.000000`, `₡520,000.00`).

## 7. Create a CRC supplier quote (User Story 2)

Add another quote with currency `CRC` and amount `750000.00`. The form does NOT show the conversion preview area. Save and confirm: no rate snapshot, no conversion indicator anywhere in the UI.

## 8. Reviewer view (User Story 4)

Sign in as a reviewer. Open the same request summary. You should see:

- The USD quote: `$1,000.00 USD` + `(₡520,000.00 CRC)` with a small ⓘ tooltip showing `Tipo de cambio aplicado: 1 USD = ₡520 (Compra, vigente 2026-05-06)`.
- The CRC quote: `₡750,000.00 CRC` only — no tooltip.
- Request total: `₡1,270,000.00 CRC` (sum of converted lines).

## 9. Generate the agreement PDF (User Story 5)

Approve the request and trigger PDF generation. Open the PDF:

- All amounts shown in CRC.
- Under the line that came from USD: a small note `Conversión: 1 USD = ₡520.000000 (Tipo Compra, vigente desde 2026-05-06)`.
- The CRC line has no note.

Re-generate the PDF later (or in a different deploy). The values and dates on each line are byte-for-byte identical for monetary content (FR-026 value-stability).

## 10. PDF refusal on missing snapshot (User Story 6 + edge case)

Insert a synthetic legacy USD quotation without snapshot fields:

```sql
UPDATE dbo.Quotations
   SET LegacyNeedsReview = 1,
       SnapshotRateValue = NULL,
       SnapshotRateType = NULL,
       SnapshotEffectiveAtUtc = NULL,
       SnapshotRateId = NULL,
       ConvertedCrcAmount = NULL
 WHERE Id = <quote-id>;
```

Attempt to generate the PDF. The agreement controller catches the
`MissingConversionMetadataException` thrown by
`SyncfusionFundingAgreementPdfRenderer.RenderFromModelAsync`, writes a
structured log entry naming the offending quotation ids, and **re-renders the
agreement Details view directly** (no `TempData` redirect — a hard reload still
shows the inline error). The user-visible message reads:

> No se puede generar el PDF: una o más cotizaciones no tienen tipo de cambio aplicado. Contacte a un administrador para asignar tipos históricos.

In **Admin → Cotizaciones Pendientes** (`/Admin/AdminLegacyQuotations`), attach
a historical rate to the quotation. The flag clears, the snapshot is set, and
PDF generation now succeeds (FR-033).

## 11. Run E2E tests

```bash
dotnet test tests/FundingPlatform.Tests.E2E
```

The seven story-aligned classes listed in `plan.md` exercise the flows above end-to-end against an ephemeral Aspire stack.

## Database inspection cheatsheet

```sql
-- Active currencies
SELECT * FROM Currencies WHERE IsEnabled = 1 ORDER BY DisplayOrder;

-- Latest USD↔CRC rate
SELECT TOP 1 * FROM ExchangeRates
 WHERE SourceCurrencyCode = 'USD' AND TargetCurrencyCode = 'CRC'
 ORDER BY EffectiveAtUtc DESC;

-- Quotes that snapshot a given rate
SELECT * FROM dbo.Quotations WHERE SnapshotRateId = @rateId;

-- Legacy review queue
SELECT * FROM dbo.Quotations WHERE LegacyNeedsReview = 1;
```
