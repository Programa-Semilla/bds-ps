using FundingPlatform.Domain.Entities;
using FundingPlatform.Domain.ValueObjects;

namespace FundingPlatform.Application.Interfaces;

/// <summary>
/// Spec 015 / US3 — administrator surface for publishing reference rates.
/// PUT/DELETE are intentionally absent: rates are immutable once snapshotted by
/// a Quotation (FR-008). Edits to a rate are modelled as a new
/// <see cref="ExchangeRate"/> row with a later <c>EffectiveAtUtc</c>.
/// </summary>
public interface IExchangeRateService
{
    Task<ExchangeRate> CreateAsync(
        CurrencyCode source,
        CurrencyCode target,
        decimal buy,
        decimal sell,
        DateTime effectiveAtUtc,
        string actorUserId,
        CancellationToken ct = default);

    Task<IReadOnlyList<ExchangeRate>> ListAsync(
        CurrencyCode source,
        CurrencyCode target,
        CancellationToken ct = default);

    /// <summary>
    /// Spec 015 / FR-008 / FR-010 — admin issued PUT against an immutable rate.
    /// Always blocked at the controller; this method records the audit entry.
    /// </summary>
    Task RecordEditAttemptAsync(Guid rateId, string actorUserId);

    /// <summary>
    /// Spec 015 / FR-008 / FR-010 — admin issued DELETE against an immutable rate.
    /// Always blocked at the controller; this method records the audit entry.
    /// </summary>
    Task RecordDeleteAttemptAsync(Guid rateId, string actorUserId);
}
