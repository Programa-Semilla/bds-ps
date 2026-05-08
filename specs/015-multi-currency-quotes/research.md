# Research: Suppliers Quotes Multi-Currency

All open clarifications were resolved during `/speckit-clarify` (Q1–Q4 in spec.md). This document captures the technical research that informs the plan.

## R1. Decimal arithmetic and rounding in C#

**Decision**: Use `System.Decimal` end-to-end in the conversion path. Final rounding via `Math.Round(value, 2, MidpointRounding.AwayFromZero)` on the computed CRC amount. Reject any code path in this feature that uses `double` or `float`.

**Rationale**: `decimal` is a 128-bit fixed-point type with 28–29 significant digits and no binary-fraction error. `MidpointRounding.AwayFromZero` is the .NET name for "half-away-from-zero" required by FR-020.

**Alternatives considered**:
- `double` with manual scaling — rejected: violates FR-020 explicitly and creates audit risk.
- Banker's rounding (`MidpointRounding.ToEven`) — rejected: spec mandates half-away-from-zero.
- Third-party `Money` library (e.g., NodaMoney) — rejected: unnecessary dependency for two currencies; constitution prefers minimal external libs.

**SQL mapping**: `decimal(18, 2)` for monetary amounts (CRC, USD), `decimal(18, 6)` for rate values. EF Core `HasPrecision(18, 2)` / `HasPrecision(18, 6)`.

## R2. Immutable-once-used enforcement on `ExchangeRate`

**Decision**: Two-layer guard.
1. **Domain**: `ExchangeRate` exposes only `MarkUsed()` as a mutator. Any other change throws.
2. **Persistence**: A unique `(SourceCurrency, TargetCurrency, EffectiveAtUtc)` index on `ExchangeRates`. Updates that change buy/sell/effective on a row with `IsUsed = 1` are rejected by domain layer; deletes blocked by `OnDelete(DeleteBehavior.Restrict)` from `SupplierQuotes.SnapshotRateId` foreign key.

**Rationale**: Domain enforcement keeps logic where business rules live (Constitution II). Persistence enforcement is defense in depth and makes accidental SQL changes hard.

**Alternatives considered**:
- Schema-level CHECK constraint on `IsUsed` — partially possible but cumbersome to express idempotently.
- Append-only-with-versioning — overkill for two currencies and a single rate-pair MVP.

## R3. Snapshot embedding strategy

**Decision**: Embed snapshot fields directly on `SupplierQuotes` (`SnapshotRateValue`, `SnapshotRateType`, `SnapshotEffectiveAtUtc`) **and** keep a foreign key `SnapshotRateId` pointing to the source `ExchangeRates` row.

**Rationale**:
- FR-026 demands value-stability — the embedded fields make the PDF and historical UI immune to a `JOIN`-by-id silently producing different numbers if the source row were ever altered.
- The FK preserves auditability (FR-036): "which quotes used rate R" remains a trivial query.
- Storage cost is negligible (~30 bytes per quote line) compared with the lookup-cost savings on hot read paths (quote lists, dashboards).

**Alternatives considered**:
- Pointer-only (FK to rate, no embedded values) — rejected: violates value-stability if the source row is ever corrupted; harder to render a PDF that has been moved between databases or restored from backup.
- Embedded-only (no FK) — rejected: makes "which quotes used rate R" auditing painful and breaks legacy-quote rate-attach use case.

## R4. Conversion preview endpoint

**Decision**: Server-computed preview. The browser sends `(currencyCode, amount)` to `POST /SupplierQuotes/Convert` (anti-forgery-protected, authenticated). Server returns `{ convertedCrc, rateValue, rateType, effectiveAtUtc, rateRecordId }`. JavaScript renders the result; **no client-side multiplication** occurs.

**Rationale**: FR-019 forbids manual override. The simplest enforcement is to never let the client compute the value. Single round-trip per amount-edit blur (debounced) is acceptable at the expected scale.

**Alternatives considered**:
- Client-side rate caching with periodic refresh — rejected: introduces inconsistency window and complexity; FR-015 already mandates server-side computation at save anyway.
- WebSocket push of latest rate — overkill for ~daily admin updates.

**Throttling**: Standard MVC anti-forgery + `[Authorize]`. No new rate-limit middleware introduced; existing per-endpoint middleware applies.

## R5. PDF rendering pattern

**Decision**: Extend the existing Razor partial used by `AgreementPdfRenderer`. The view's data contract gains a per-line `OriginalAmount`, `OriginalCurrencyCode`, and `RateSnapshot?` (nullable). The partial emits a small "Conversion: 1 USD = ₡X (Buy rate, effective YYYY-MM-DD)" footer line under each converted row when the snapshot is present. The renderer **throws** a domain exception when any non-CRC line lacks a snapshot, surfaced by `AgreementsController.Pdf` as inline error + log entry (FR-027).

**Rationale**: Reuses the existing Syncfusion HtmlToPdfConverter pipeline (already containerized with Chromium per spec 016). No new dependency; only template + view-model changes.

**Alternatives considered**:
- Separate "multi-currency PDF" template — rejected: doubles maintenance for a small visual delta.
- Inline JS post-processing on the rendered HTML — rejected: bypasses Syncfusion's deterministic render.

## R6. Migration / post-deploy strategy

**Decision**: Idempotent post-deploy SQL script `015-currency-seed.sql` does, in order:
1. `MERGE` into `Currencies` for CRC (base, enabled, non-disable-able) and USD (enabled).
2. For each existing `SupplierQuotes` row:
   - If currency column is null/CRC: set `OriginalCurrencyCode = 'CRC'`, `OriginalAmount = TotalAmount`, `ConvertedCrcAmount = TotalAmount`, leave snapshot fields null, `LegacyNeedsReview = 0`.
   - Else (non-CRC, no snapshot): set `OriginalCurrencyCode = <existing currency text>`, `OriginalAmount = <existing>`, `ConvertedCrcAmount = NULL`, `LegacyNeedsReview = 1`.
3. Idempotent guard: only operate on rows where the new columns are still default (NULL).

**Rationale**: dacpac post-deploy scripts run on every deploy, so idempotency is mandatory (Constitution IV). Running it twice MUST be a no-op.

**Alternatives considered**:
- One-shot manual data fix — rejected: not reproducible, breaks dev parity.
- Application-level migration — rejected: violates schema-first principle.

## R7. Concurrency on rate creation

**Decision**: Rely on the unique index `(SourceCurrency, TargetCurrency, EffectiveAtUtc)` to surface duplicate-timestamp conflicts. The `ExchangeRateService.Create()` catches `DbUpdateException` whose inner `SqlException.Number` matches duplicate-key (2627/2601) and returns a domain validation error mapped to FR-007's "rate at this timestamp already exists".

**Rationale**: Keeps the racing-admin case correct without explicit locking. Last-writer-wins for non-conflicting timestamps is acceptable per spec edge cases.

## R8. Caching the "latest rate" read

**Decision**: No caching layer in MVP. A single indexed query `SELECT TOP 1 ... ORDER BY EffectiveAtUtc DESC` from `ExchangeRates` filtered by currency pair is the read path on each conversion preview / save.

**Rationale**: Rate creation is rare (~daily). Read volume is bounded by quote-form interaction. Adding a cache adds invalidation complexity (Constitution VI). Revisit if profiling shows hot-path pressure.

**Alternatives considered**:
- In-memory `IMemoryCache` keyed by pair, invalidated on rate insert — possible future optimization, deferred.

## R9. Localization

**Decision**: All new admin-side and applicant-side UI copy ships in **es-CR primary** following spec 012's localization story. Currency formatting uses `CultureInfo("es-CR")` for CRC and `CultureInfo("en-US")` for USD display, with explicit `string.Format(culture, "{0:C}", amount)`. Conversion-indicator tooltip text is keyed in the existing resx.

**Rationale**: Spec 012 already established the localization pipeline; this feature inherits it.

## R10. Deferred — out of MVP scope

For traceability the following are **explicitly deferred** (matching spec's "Out of scope"):

- More than two currencies; multi-pair rate matrix.
- Manual per-quote conversion override.
- Re-pricing existing quotes against a newer rate.
- Rate approval / draft workflow.
- Stale-rate notifications / dashboards.
- Future-dated effective timestamps and scheduled rates.
- Mid-quote currency switching that preserves the entered amount (currency is fixed at save).
