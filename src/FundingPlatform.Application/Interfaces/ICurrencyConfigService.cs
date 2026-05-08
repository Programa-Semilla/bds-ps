using FundingPlatform.Domain.Entities;
using FundingPlatform.Domain.ValueObjects;

namespace FundingPlatform.Application.Interfaces;

/// <summary>
/// Spec 015 / US3 — administrator surface for the two-row currency catalog
/// (CRC + USD in MVP). Enforces the CRC-permanent invariant and writes an
/// audit-log entry on every state change.
/// </summary>
public interface ICurrencyConfigService
{
    Task EnableAsync(CurrencyCode code, string actorUserId, CancellationToken ct = default);
    Task DisableAsync(CurrencyCode code, string actorUserId, CancellationToken ct = default);
    Task<IReadOnlyList<Currency>> ListEnabledAsync(CancellationToken ct = default);
    Task<IReadOnlyList<Currency>> ListAllAsync(CancellationToken ct = default);
}
