# Data Model: Suppliers Quotes Multi-Currency

All schema is owned by the `FundingPlatform.Database` dacpac. EF Core configures mappings only; no migrations.

## New tables

### `Currencies`

| Column | Type | Constraint | Notes |
|---|---|---|---|
| `Code` | `char(3)` | PK | ISO 4217 (`CRC`, `USD`). |
| `Symbol` | `nvarchar(8)` | NOT NULL | e.g. `₡`, `$`. |
| `DisplayName` | `nvarchar(64)` | NOT NULL | e.g. `Costa Rican colón`. |
| `DecimalPrecision` | `tinyint` | NOT NULL DEFAULT `2` | Always `2` in MVP. |
| `IsEnabled` | `bit` | NOT NULL DEFAULT `1` | CRC must remain `1` always (CHECK). |
| `IsBaseCurrency` | `bit` | NOT NULL DEFAULT `0` | Exactly one row has `1` (CHECK + filtered unique index). |
| `DisplayOrder` | `smallint` | NOT NULL | UI ordering. |
| `RowVersion` | `rowversion` | NOT NULL | Optimistic concurrency. |

**Constraints**:

- `CK_Currencies_BaseAlwaysEnabled`: `IsBaseCurrency = 0 OR IsEnabled = 1`.
- Filtered unique index `UQ_Currencies_OneBase` on `IsBaseCurrency` where `IsBaseCurrency = 1`.

**Seed (post-deploy, idempotent MERGE)**:

| Code | Symbol | DisplayName | IsEnabled | IsBaseCurrency | DisplayOrder |
|---|---|---|---|---|---|
| `CRC` | `₡` | Costa Rican colón | 1 | 1 | 1 |
| `USD` | `$` | US dollar | 1 | 0 | 2 |

### `ExchangeRates`

| Column | Type | Constraint | Notes |
|---|---|---|---|
| `Id` | `uniqueidentifier` | PK DEFAULT `NEWSEQUENTIALID()` | |
| `SourceCurrencyCode` | `char(3)` | NOT NULL FK → `Currencies(Code)` | Pair direction: source × rate = target. |
| `TargetCurrencyCode` | `char(3)` | NOT NULL FK → `Currencies(Code)` | |
| `BuyRate` | `decimal(18, 6)` | NOT NULL | CRC per 1 USD (per spec clarification Q1). |
| `SellRate` | `decimal(18, 6)` | NOT NULL | Captured for audit; not applied in MVP. |
| `EffectiveAtUtc` | `datetime2(3)` | NOT NULL | Millisecond precision, narrows the duplicate-timestamp collision window for concurrent admin saves. Must be `<= SYSUTCDATETIME()` at insert (CHECK at app layer). |
| `CreatedByUserId` | `nvarchar(450)` | NOT NULL FK → `AspNetUsers(Id)` | Audit. |
| `CreatedAtUtc` | `datetime2(3)` | NOT NULL DEFAULT `SYSUTCDATETIME()` | |
| `IsUsed` | `bit` | NOT NULL DEFAULT `0` | Set to 1 the first time a quote snapshots this rate. |
| `RowVersion` | `rowversion` | NOT NULL | |

**Constraints**:

- `CK_ExchangeRates_PositiveBuy`: `BuyRate > 0`.
- `CK_ExchangeRates_PositiveSell`: `SellRate > 0`.
- `CK_ExchangeRates_DistinctPair`: `SourceCurrencyCode <> TargetCurrencyCode`.
- Unique `UQ_ExchangeRates_PairAt` on `(SourceCurrencyCode, TargetCurrencyCode, EffectiveAtUtc)`.
- Index `IX_ExchangeRates_PairEffectiveAtDesc` on `(SourceCurrencyCode, TargetCurrencyCode, EffectiveAtUtc DESC)` to power latest-rate reads.

## Extended table

### `Quotations` — new columns (existing table)

The existing table already has `Currency NVARCHAR(3) NULL` and `Price DECIMAL(18,2) NOT NULL`. These keep their names and become the canonical "original" fields. New columns are added alongside.

| Column | Type | Constraint | Notes |
|---|---|---|---|
| `Currency` (existing) | `char(3)` | NOT NULL after migration; FK added → `Currencies(Code)` | Was `nvarchar(3) NULL`. Migration sets `'CRC'` on any null rows, then alters to NOT NULL char(3) and adds the FK. This **is** the OriginalCurrencyCode. |
| `Price` (existing) | `decimal(18, 2)` | NOT NULL | Unchanged. This **is** the OriginalAmount. |
| `ConvertedCrcAmount` | `decimal(18, 2)` | NULL | Equal to `Price` for CRC; null for legacy unreviewed; computed for non-CRC. |
| `SnapshotRateValue` | `decimal(18, 6)` | NULL | Embedded snapshot — rate value applied. |
| `SnapshotRateType` | `tinyint` | NULL | `1 = Buy`, `2 = Sell`. MVP uses `Buy` only. |
| `SnapshotEffectiveAtUtc` | `datetime2(3)` | NULL | Effective timestamp of the snapshotted rate (matches `ExchangeRates.EffectiveAtUtc` precision). |
| `SnapshotRateId` | `uniqueidentifier` | NULL FK → `ExchangeRates(Id)` `ON DELETE NO ACTION` | Audit pointer. |
| `LegacyNeedsReview` | `bit` | NOT NULL DEFAULT `0` | Set by migration for pre-existing non-CRC rows lacking snapshot. |

**Constraints**:

- `CK_Quotations_CrcSnapshotMustBeNull`: when `Currency = 'CRC'`, all `Snapshot*` columns are NULL and `LegacyNeedsReview = 0`.
- `CK_Quotations_NonCrcRequiresSnapshot`: when `Currency <> 'CRC'` and `LegacyNeedsReview = 0`, all four `Snapshot*` columns are NOT NULL and `ConvertedCrcAmount` is NOT NULL.

**Indexes**:

- `IX_Quotations_LegacyNeedsReview` on `LegacyNeedsReview` where `LegacyNeedsReview = 1` (admin queue).
- `IX_Quotations_SnapshotRateId` on `SnapshotRateId` (FK lookup support).

## Domain model (C#)

```csharp
public sealed record CurrencyCode(string Value) {
    public static readonly CurrencyCode Crc = new("CRC");
    public static readonly CurrencyCode Usd = new("USD");
    public bool IsBase => this == Crc;
}

public sealed class Currency {
    public CurrencyCode Code { get; }
    public string Symbol { get; }
    public string DisplayName { get; }
    public byte DecimalPrecision { get; }
    public bool IsEnabled { get; private set; }
    public bool IsBaseCurrency { get; }
    public short DisplayOrder { get; private set; }

    public void Enable();
    public void Disable();      // throws if IsBaseCurrency
}

public enum RateType : byte { Buy = 1, Sell = 2 }

public sealed class ExchangeRate {
    public Guid Id { get; }
    public CurrencyCode SourceCurrency { get; }
    public CurrencyCode TargetCurrency { get; }
    public decimal BuyRate { get; }
    public decimal SellRate { get; }
    public DateTime EffectiveAtUtc { get; }
    public string CreatedByUserId { get; }
    public DateTime CreatedAtUtc { get; }
    public bool IsUsed { get; private set; }

    public ExchangeRate(/* validates: positive buy/sell, distinct pair, effective <= now */);
    public decimal ConvertUsdToCrc(decimal usdAmount);  // usdAmount * BuyRate, rounded half-away-from-zero to 2dp
    public ExchangeRateSnapshot ToSnapshot(RateType type);
    public void MarkUsed();                              // idempotent: no-op if already used
}

public sealed record ExchangeRateSnapshot(
    Guid RateRecordId,
    decimal RateValue,
    RateType RateType,
    DateTime EffectiveAtUtc);

public partial class Quotation {
    // existing fields: Id, ItemId, SupplierId, SupplierBranchId, Price, ValidUntil, DocumentId, Currency, CreatedAt
    // Currency and Price stay; new fields below.
    public decimal? ConvertedCrcAmount { get; private set; }
    public ExchangeRateSnapshot? Snapshot { get; private set; }
    public bool LegacyNeedsReview { get; private set; }

    // NEW save path. Replaces the legacy free-text constructor's role for new code paths.
    // For CRC: snapshots null, ConvertedCrcAmount = price.
    // For USD: pulls latest rate via IConversionService, computes ConvertedCrcAmount, sets Snapshot, marks rate used.
    public void SetCurrencyAndAmount(CurrencyCode currency, decimal price, IConversionService conversion);

    // Amount-only edit: re-applies the existing Snapshot to recompute ConvertedCrcAmount.
    // Throws if Snapshot is null but Currency != CRC (the legacy-needs-review case).
    public void EditAmount(decimal newPrice);

    // Currency change with re-conversion (FR-017a). Clears existing snapshot and re-applies a new one.
    public void ChangeCurrency(CurrencyCode newCurrency, IConversionService conversion);

    // Admin attaches a historical rate to a flagged legacy quotation.
    public void AttachLegacyRate(ExchangeRateSnapshot snapshot, decimal convertedCrc);

    // EXISTING method — keep, but new code paths SHOULD use SetCurrencyAndAmount or ChangeCurrency.
    // Internally we mark this as obsolete in a follow-up but do not break callers in this MVP.
    public void EditCurrency(string code);
}
```

## Read models

- **Latest applicable rate**: `EXEC sp_executesql N'SELECT TOP 1 * FROM ExchangeRates WHERE SourceCurrencyCode = @s AND TargetCurrencyCode = @t ORDER BY EffectiveAtUtc DESC'` — backed by `IX_ExchangeRates_PairEffectiveAtDesc`.
- **Legacy queue**: `SELECT * FROM Quotations WHERE LegacyNeedsReview = 1` — backed by filtered index.
- **Audit "which quotes used rate R"**: `SELECT * FROM Quotations WHERE SnapshotRateId = @id`.

## State transitions

### `Currency.IsEnabled`
```
Enabled -> Disabled (allowed only when IsBaseCurrency = 0)
Disabled -> Enabled
```

### `ExchangeRate.IsUsed`
```
false -> true   (one-way; happens automatically on first quote that snapshots it)
```

### `Quotation` snapshot lifecycle
```
[CRC quote]            : Original=Converted, Snapshot=null, Legacy=0
[USD quote, new flow]  : Original entered, Snapshot set on save, Converted computed, Legacy=0
[USD quote, legacy]    : Original entered, Snapshot=null, Converted=null, Legacy=1
                          -- AttachLegacyRate -->
[USD quote, normalized]: Snapshot set, Converted set, Legacy=0
```

## Audit log additions

The existing `AuditLog` table (assumed to follow project convention `AuditLogs(Id, ActorUserId, EventType, EntityType, EntityId, BeforeJson, AfterJson, Outcome, OccurredAtUtc)`) gains new `EventType` values:

- `Currency.Enabled` / `Currency.Disabled`
- `ExchangeRate.Created`
- `ExchangeRate.EditAttemptBlocked`
- `ExchangeRate.DeleteAttemptBlocked`
- `Quotation.LegacyRateAttached`

No schema change to `AuditLogs`; only new event-type strings.
