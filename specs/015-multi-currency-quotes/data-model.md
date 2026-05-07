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
| `EffectiveAtUtc` | `datetime2(0)` | NOT NULL | Must be `<= SYSUTCDATETIME()` at insert (CHECK at app layer). |
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

### `SupplierQuotes` — new columns (existing table)

| Column | Type | Constraint | Notes |
|---|---|---|---|
| `OriginalCurrencyCode` | `char(3)` | NOT NULL FK → `Currencies(Code)` | Set on every row by migration. |
| `OriginalAmount` | `decimal(18, 2)` | NOT NULL | The amount the user typed. |
| `ConvertedCrcAmount` | `decimal(18, 2)` | NULL | Equal to `OriginalAmount` for CRC; null for legacy unreviewed; computed for non-CRC. |
| `SnapshotRateValue` | `decimal(18, 6)` | NULL | Embedded snapshot — rate value applied. |
| `SnapshotRateType` | `tinyint` | NULL | `1 = Buy`, `2 = Sell`. MVP uses `Buy` only. |
| `SnapshotEffectiveAtUtc` | `datetime2(0)` | NULL | Effective timestamp of the snapshotted rate. |
| `SnapshotRateId` | `uniqueidentifier` | NULL FK → `ExchangeRates(Id)` `ON DELETE NO ACTION` | Audit pointer. |
| `LegacyNeedsReview` | `bit` | NOT NULL DEFAULT `0` | Set by migration for pre-existing non-CRC rows lacking snapshot. |

**Constraints**:

- `CK_SupplierQuotes_CrcSnapshotMustBeNull`: when `OriginalCurrencyCode = 'CRC'`, all `Snapshot*` columns are NULL and `LegacyNeedsReview = 0`.
- `CK_SupplierQuotes_NonCrcRequiresSnapshot`: when `OriginalCurrencyCode <> 'CRC'` and `LegacyNeedsReview = 0`, all four `Snapshot*` columns are NOT NULL and `ConvertedCrcAmount` is NOT NULL.

**Indexes**:

- `IX_SupplierQuotes_LegacyNeedsReview` on `LegacyNeedsReview` where `LegacyNeedsReview = 1` (admin queue).
- `IX_SupplierQuotes_SnapshotRateId` on `SnapshotRateId` (FK lookup support).

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

public partial class SupplierQuote {
    // existing fields …
    public CurrencyCode OriginalCurrency { get; private set; }
    public decimal OriginalAmount { get; private set; }
    public decimal? ConvertedCrcAmount { get; private set; }
    public ExchangeRateSnapshot? Snapshot { get; private set; }
    public bool LegacyNeedsReview { get; private set; }

    public void SetCurrencyAndAmount(CurrencyCode currency, decimal amount, IConversionService conversion);
    public void EditAmount(decimal newAmount);          // re-applies existing Snapshot; throws if currency change attempted
    public void AttachLegacyRate(ExchangeRateSnapshot snapshot, decimal convertedCrc); // clears LegacyNeedsReview
}
```

## Read models

- **Latest applicable rate**: `EXEC sp_executesql N'SELECT TOP 1 * FROM ExchangeRates WHERE SourceCurrencyCode = @s AND TargetCurrencyCode = @t ORDER BY EffectiveAtUtc DESC'` — backed by `IX_ExchangeRates_PairEffectiveAtDesc`.
- **Legacy queue**: `SELECT * FROM SupplierQuotes WHERE LegacyNeedsReview = 1` — backed by filtered index.
- **Audit "which quotes used rate R"**: `SELECT * FROM SupplierQuotes WHERE SnapshotRateId = @id`.

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

### `SupplierQuote` snapshot lifecycle
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
- `SupplierQuote.LegacyRateAttached`

No schema change to `AuditLogs`; only new event-type strings.
