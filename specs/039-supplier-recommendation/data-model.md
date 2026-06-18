# Phase 1 Data Model: Supplier Recommendation Algorithm Rewrite

**Spec:** spec.md | **Research:** research.md | **Date:** 2026-06-18

This feature adds two value-typed fields to one existing entity and introduces one enum and one value object. The recommendation itself is **computed, not persisted** (D7) — its "model" is the in-memory result shape, documented last.

---

## 1. New enum: `DurationUnit`

`src/FundingPlatform.Domain/Enums/DurationUnit.cs`

| Member | Value | Label (es-CR) |
|---|---|---|
| `Days` | 1 | días |
| `Months` | 2 | meses |

- Stored as `TINYINT` via `HasConversion<byte>` (mirrors slice-A status enums).
- Labels live in a display map (no Spanish literals in JS; `[Display]`/resource for the dropdown).

---

## 2. New value object: `TimeDuration`

`src/FundingPlatform.Domain/ValueObjects/TimeDuration.cs`

```text
TimeDuration(int Value, DurationUnit Unit)
  invariant: Value > 0
  invariant: Unit is a defined DurationUnit
  computed:  int InDays => Unit == DurationUnit.Months ? Value * 30 : Value
```

- Immutable record. Construction throws `ArgumentException` for `Value <= 0` or an undefined unit.
- `InDays` uses the 30-days-per-month normalization (research D5) — comparison-only, not persisted.
- Reused for both delivery lead time and warranty.

---

## 3. Modified entity: `Quotation`

`src/FundingPlatform.Domain/Entities/Quotation.cs`

### New fields

| Field | Type | Rule |
|---|---|---|
| `DeliveryLeadTime` | `TimeDuration` | Required; `Value > 0`; unit days/months |
| `Warranty` | `TimeDuration` | Required; `Value > 0`; unit days/months |

- Both set in the constructor (new required parameters) and via a mutator used by the spec-023 edit path (`SetDeliveryAndWarranty(TimeDuration delivery, TimeDuration warranty)` or extend the existing edit surface).
- `DeliveryLeadTime.InDays` / `Warranty.InDays` are the comparison keys used by scoring.
- Price comparison key (existing): `ConvertedCrcAmount ?? Price` (research D6).

### EF configuration — `QuotationConfiguration.cs`

Map each `TimeDuration` as an owned type (mirrors the `Snapshot` `OwnsOne` pattern at lines 40-53), to flat columns:

```text
OwnsOne(q => q.DeliveryLeadTime, d => {
    d.Property(x => x.Value).HasColumnName("DeliveryLeadTimeValue").IsRequired();
    d.Property(x => x.Unit ).HasColumnName("DeliveryLeadTimeUnit").HasConversion<byte>().IsRequired();
});
OwnsOne(q => q.Warranty, w => {
    w.Property(x => x.Value).HasColumnName("WarrantyValue").IsRequired();
    w.Property(x => x.Unit ).HasColumnName("WarrantyUnit").HasConversion<byte>().IsRequired();
});
```
(Owned types are non-null required; the entity guarantees they are always set.)

### dacpac — `src/FundingPlatform.Database/Tables/dbo.Quotations.sql`

Add four columns, **NOT NULL with placeholder DEFAULTs** (research D8):

```sql
[DeliveryLeadTimeValue] INT     NOT NULL CONSTRAINT [DF_Quotations_DeliveryLeadTimeValue] DEFAULT (1),
[DeliveryLeadTimeUnit]  TINYINT NOT NULL CONSTRAINT [DF_Quotations_DeliveryLeadTimeUnit]  DEFAULT (1),
[WarrantyValue]         INT     NOT NULL CONSTRAINT [DF_Quotations_WarrantyValue]         DEFAULT (1),
[WarrantyUnit]          TINYINT NOT NULL CONSTRAINT [DF_Quotations_WarrantyUnit]          DEFAULT (1),
```

Optional CHECK constraints: `DeliveryLeadTimeValue > 0`, `WarrantyValue > 0`, units `IN (1,2)`.

### Seed-data update (dacpac post-deploy)

Update the seeded quotation INSERTs / a post-deploy script so demo/seed quotations carry realistic, *varied* delivery and warranty values (so the recommendation demo shows a non-lowest-price winner, SC-001). The E2E `SeedUser`/seeding path that creates quotations must also supply the new fields.

---

## 4. Consumed (unchanged) — provider compliance from slice A

`Supplier` (read-only here): `HaciendaStatus?`, `CcssStatus?`, `SicopStatus?`, `IsPmeOrPyme`. Favorability via `RegulatoryStatusFavorability` (`HaciendaStatus.AlDia`, `CcssStatus.AlDia`, `SicopStatus.SinSanciones`). Hard block value: `CcssStatus.SinInscripcion`. `null` status = unreviewed = scores 1, **not** a block (research D4).

---

## 5. Rewritten value object: `SupplierScore` (computed result)

`src/FundingPlatform.Domain/ValueObjects/SupplierScore.cs` — record expanded; `ComputeForItem` rewritten.

### Per-quotation result shape (realizes §22.8 as a DTO, not a table)

| Field | Type | Meaning |
|---|---|---|
| `QuotationId` | int | (carried in the result tuple as today) |
| `SupplierId` | int | |
| `IsEligible` | bool | false when CCSS `sin inscripción` |
| `BlockReason` | enum/string? | e.g. `CcssSinInscripcion` (null when eligible) |
| `PriceScore` | int | 1 or 2 |
| `DeliveryLeadTimeScore` | int | 1 or 2 |
| `WarrantyTimeScore` | int | 1 or 2 |
| `HaciendaScore` | int | 1 or 2 |
| `CcssScore` | int | 1 or 2 |
| `SicopScore` | int | 1 or 2 |
| `PmeOrPymeScore` | int | 1 or 2 |
| `Total` | int | sum (eligible: 7–14); ineligible → not scored |
| `IsRecommended` | bool | true only for the strict single max among eligible |
| `IsTiedAtTop` | bool | true for each provider in a top-score tie |

### Item-level result

`ComputeForItem` returns the per-quotation results plus a derivable `HasRecommendationTie` (≥2 eligible share max) and `HasAnyEligible` (false → "ningún proveedor elegible"). Surface these via the item view-model.

### Algorithm (per item)

```text
candidates = quotations
eligible   = candidates where Supplier.CcssStatus != SinInscripcion
ineligible = candidates where Supplier.CcssStatus == SinInscripcion
            → IsEligible=false, BlockReason=CcssSinInscripcion, no scores, IsRecommended=false

over `eligible` only:
  priceKey(q)    = q.ConvertedCrcAmount ?? q.Price
  minPrice       = min priceKey
  priceTie       = (#eligible with priceKey==minPrice) >= 2
  PriceScore     = priceTie ? 1 : (priceKey==minPrice ? 2 : 1)        # tie → all 1

  minDeliveryDays = min DeliveryLeadTime.InDays
  DeliveryScore   = (InDays==minDeliveryDays) ? 2 : 1                  # tie → all 2

  maxWarrantyDays = max Warranty.InDays
  WarrantyScore   = (InDays==maxWarrantyDays) ? 2 : 1                  # tie → all 2

  HaciendaScore = Supplier.HaciendaStatus == AlDia        ? 2 : 1
  CcssScore     = Supplier.CcssStatus     == AlDia        ? 2 : 1
  SicopScore    = Supplier.SicopStatus    == SinSanciones ? 2 : 1
  PmeOrPymeScore= Supplier.IsPmeOrPyme                     ? 2 : 1

  Total = sum of the 7 criterion scores

maxTotal      = max Total over eligible (if any)
winners       = eligible with Total==maxTotal
IsRecommended = (winners.Count == 1) ? (q in winners) : false
IsTiedAtTop   = (winners.Count >= 2) && (q in winners)
HasRecommendationTie = winners.Count >= 2
HasAnyEligible       = eligible.Count >= 1
```

---

## 6. DTO / view-model changes (no persistence)

- `ReviewQuotationDto` (`Application/DTOs/ReviewApplicationDto.cs`): replace `int Score` + 4 bools with the seven criterion scores, `Total`, `IsRecommended`, `IsEligible`, `BlockReason`, and the raw `DeliveryLeadTimeValue/Unit`, `WarrantyValue/Unit`.
- `ReviewQuotationViewModel` (`Web/ViewModels/ReviewApplicationViewModel.cs`): mirror the DTO.
- Item-level review VM: add `HasRecommendationTie`, `HasAnyEligible`.
- `AddSupplierViewModel` + the spec-023 quotation-edit VM (both `IQuoteFieldsModel`): add `DeliveryLeadTimeValue`, `DeliveryLeadTimeUnit`, `WarrantyValue`, `WarrantyUnit` with `[Required]` + `[Range(1, …)]` es-CR messages.

---

## 7. Relationships (unchanged)

`Application 1—* Item 1—* Quotation *—1 Supplier 1—* SupplierBranch`. No FK or cardinality changes. The recommendation is computed per `Item` across its `Quotation`s.
