using FundingPlatform.Application.Interfaces;
using FundingPlatform.Domain.Entities;
using FundingPlatform.Domain.Interfaces;
using FundingPlatform.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace FundingPlatform.Application.Services;

/// <summary>
/// Spec 015 / US3 / T310 — administrator surface for the currency catalog
/// (CRC + USD in MVP). CRC is the platform's permanent base currency: it must
/// always be enabled and cannot be disabled. The service writes a structured
/// audit-log entry on every state change using the
/// <see cref="MultiCurrencyAuditActions"/> constants.
///
/// <para>
/// The audit channel here is a structured <see cref="ILogger"/> entry rather
/// than the per-application <c>VersionHistory</c> aggregate (which is scoped
/// to a single Application). Currency-catalog changes are platform-global
/// and have no Application context, so the structured log is the appropriate
/// surface for now.
/// </para>
/// </summary>
public class CurrencyConfigService : ICurrencyConfigService
{
    private readonly ICurrencyRepository _currencies;
    private readonly ILogger<CurrencyConfigService> _logger;

    public CurrencyConfigService(
        ICurrencyRepository currencies,
        ILogger<CurrencyConfigService> logger)
    {
        _currencies = currencies;
        _logger = logger;
    }

    public async Task EnableAsync(CurrencyCode code, string actorUserId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(actorUserId);

        var currency = await _currencies.GetByCodeAsync(code, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                $"Currency '{code}' is not configured.");

        var wasEnabled = currency.IsEnabled;
        currency.Enable();
        await _currencies.SaveChangesAsync(ct).ConfigureAwait(false);

        // Idempotent: still emit the audit entry so an admin's intent is recorded
        // even when the row was already enabled.
        _logger.LogInformation(
            "AuditEvent {AuditAction} actorUserId={ActorUserId} currencyCode={CurrencyCode} previouslyEnabled={WasEnabled}",
            MultiCurrencyAuditActions.CurrencyEnabled, actorUserId, code.Value, wasEnabled);
    }

    public async Task DisableAsync(CurrencyCode code, string actorUserId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(actorUserId);

        var currency = await _currencies.GetByCodeAsync(code, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                $"Currency '{code}' is not configured.");

        // Domain entity throws InvalidOperationException when IsBaseCurrency is true.
        // Let it propagate to the caller — the controller maps to the FR-002 message.
        currency.Disable();
        await _currencies.SaveChangesAsync(ct).ConfigureAwait(false);

        _logger.LogInformation(
            "AuditEvent {AuditAction} actorUserId={ActorUserId} currencyCode={CurrencyCode}",
            MultiCurrencyAuditActions.CurrencyDisabled, actorUserId, code.Value);
    }

    public Task<IReadOnlyList<Currency>> ListEnabledAsync(CancellationToken ct = default)
        => _currencies.ListEnabledAsync(ct);

    public Task<IReadOnlyList<Currency>> ListAllAsync(CancellationToken ct = default)
        => _currencies.ListAllAsync(ct);
}
