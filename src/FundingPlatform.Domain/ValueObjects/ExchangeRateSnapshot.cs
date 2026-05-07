using FundingPlatform.Domain.Enums;

namespace FundingPlatform.Domain.ValueObjects;

/// <summary>
/// Spec 015 — immutable copy of the rate that was applied to a Quotation at save
/// time. Embedded on the Quotation so historical converted CRC values remain
/// stable even if the source <c>ExchangeRate</c> row is later superseded
/// (FR-013, FR-016).
///
/// <see cref="RateRecordId"/> still points back to the source row for audit
/// traceability ("which quotes used rate R").
/// </summary>
public sealed record ExchangeRateSnapshot(
    Guid RateRecordId,
    decimal RateValue,
    RateType RateType,
    DateTime EffectiveAtUtc);
