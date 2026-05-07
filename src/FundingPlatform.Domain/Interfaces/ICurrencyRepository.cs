using FundingPlatform.Domain.Entities;
using FundingPlatform.Domain.ValueObjects;

namespace FundingPlatform.Domain.Interfaces;

/// <summary>
/// Spec 015 / US3 — read/write surface for the <c>Currencies</c> catalog.
/// Two rows in MVP (CRC + USD). The repository persists state changes from
/// <see cref="Currency.Enable"/> / <see cref="Currency.Disable"/> through the
/// <see cref="ICurrencyConfigService"/>.
/// </summary>
public interface ICurrencyRepository
{
    /// <summary>Fetches a tracked entity by code, or null if not configured.</summary>
    Task<Currency?> GetByCodeAsync(CurrencyCode code, CancellationToken ct = default);

    /// <summary>Returns all currencies ordered by display order.</summary>
    Task<IReadOnlyList<Currency>> ListAllAsync(CancellationToken ct = default);

    /// <summary>Returns all enabled currencies ordered by display order.</summary>
    Task<IReadOnlyList<Currency>> ListEnabledAsync(CancellationToken ct = default);

    /// <summary>Persists pending changes on the tracked entity returned by <see cref="GetByCodeAsync"/>.</summary>
    Task SaveChangesAsync(CancellationToken ct = default);
}
