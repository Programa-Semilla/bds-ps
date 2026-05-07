using FundingPlatform.Domain.Entities;
using FundingPlatform.Domain.ValueObjects;

namespace FundingPlatform.Domain.Interfaces;

/// <summary>
/// Spec 015 — read/write surface for the <c>ExchangeRates</c> table. The
/// implementation translates SQL Server unique-key collisions (2627/2601) on
/// (SourceCurrency, TargetCurrency, EffectiveAtUtc) into a typed
/// <see cref="DuplicateRateTimestampException"/> per FR-007.
/// </summary>
public interface IExchangeRateRepository
{
    /// <summary>
    /// Returns the most recent rate for the requested pair, or null if none.
    /// Backed by <c>IX_ExchangeRates_PairEffectiveAtDesc</c>.
    /// </summary>
    Task<ExchangeRate?> GetLatestAsync(
        CurrencyCode source,
        CurrencyCode target,
        CancellationToken ct = default);

    /// <summary>Returns all rates for the pair ordered by EffectiveAtUtc descending.</summary>
    Task<IReadOnlyList<ExchangeRate>> ListByPairAsync(
        CurrencyCode source,
        CurrencyCode target,
        CancellationToken ct = default);

    /// <summary>
    /// Inserts a rate. Translates the unique-index collision on
    /// (Source, Target, EffectiveAtUtc) into <see cref="DuplicateRateTimestampException"/>.
    /// </summary>
    Task AddAsync(ExchangeRate rate, CancellationToken ct = default);

    /// <summary>Loads a rate by id (for snapshotting and audit lookups).</summary>
    Task<ExchangeRate?> GetByIdAsync(Guid id, CancellationToken ct = default);
}

/// <summary>
/// FR-007 — surfaced when an admin attempts to publish two rates with the same
/// (source, target, effectiveAt) tuple. Translated to
/// <c>UserFacingErrorCode.DuplicateRateTimestamp</c> at the Web boundary.
/// </summary>
public sealed class DuplicateRateTimestampException : Exception
{
    public DuplicateRateTimestampException(string message) : base(message) { }
    public DuplicateRateTimestampException(string message, Exception inner) : base(message, inner) { }
}
