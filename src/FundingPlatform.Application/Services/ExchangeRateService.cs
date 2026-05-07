using FundingPlatform.Application.Errors;
using FundingPlatform.Application.Interfaces;
using FundingPlatform.Domain.Entities;
using FundingPlatform.Domain.Interfaces;
using FundingPlatform.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace FundingPlatform.Application.Services;

/// <summary>
/// Spec 015 / US3 / T311 — administrator surface for publishing reference
/// rates. Rates are immutable once snapshotted by a Quotation (FR-008). The
/// service:
///
/// <list type="bullet">
/// <item>Validates input (positive buy + sell, distinct pair, non-future
/// effective timestamp) — invariants are also enforced by the
/// <see cref="ExchangeRate"/> entity, which is the source of truth. The
/// service catches the entity's <see cref="ArgumentException"/> and translates
/// to <see cref="UserFacingException"/> with the appropriate
/// <see cref="UserFacingErrorCode"/>.</item>
/// <item>Catches <see cref="DuplicateRateTimestampException"/> from the
/// repository (raised on SQL 2627/2601 collisions on the
/// <c>UQ_ExchangeRates_PairAt</c> unique index) and translates to
/// <see cref="UserFacingErrorCode.DuplicateRateTimestamp"/>.</item>
/// <item>Writes a structured audit entry on every successful create
/// (<see cref="MultiCurrencyAuditActions.ExchangeRateCreated"/>) and on every
/// blocked edit/delete attempt routed through
/// <see cref="RecordEditAttemptAsync"/> /
/// <see cref="RecordDeleteAttemptAsync"/>.</item>
/// </list>
/// </summary>
public class ExchangeRateService : IExchangeRateService
{
    private readonly IExchangeRateRepository _repository;
    private readonly ILogger<ExchangeRateService> _logger;

    public ExchangeRateService(
        IExchangeRateRepository repository,
        ILogger<ExchangeRateService> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public async Task<ExchangeRate> CreateAsync(
        CurrencyCode source,
        CurrencyCode target,
        decimal buy,
        decimal sell,
        DateTime effectiveAtUtc,
        string actorUserId,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentException.ThrowIfNullOrWhiteSpace(actorUserId);

        // Service-level pre-checks for explicit per-field error codes. The entity
        // ctor enforces the same invariants as a defence-in-depth backstop.
        if (buy <= 0m)
        {
            throw new UserFacingException(
                UserFacingErrorCode.OperationRejected,
                "BuyRate must be greater than zero.",
                fieldKey: nameof(ExchangeRate.BuyRate));
        }
        if (sell <= 0m)
        {
            throw new UserFacingException(
                UserFacingErrorCode.OperationRejected,
                "SellRate must be greater than zero.",
                fieldKey: nameof(ExchangeRate.SellRate));
        }
        if (effectiveAtUtc > DateTime.UtcNow)
        {
            throw new UserFacingException(
                UserFacingErrorCode.FutureDatedRateRejected,
                "Effective timestamp must be in the past or now.",
                fieldKey: nameof(ExchangeRate.EffectiveAtUtc));
        }
        if (source == target)
        {
            throw new UserFacingException(
                UserFacingErrorCode.OperationRejected,
                "Source and target currencies must differ.",
                fieldKey: nameof(ExchangeRate.TargetCurrency));
        }

        ExchangeRate rate;
        try
        {
            rate = new ExchangeRate(source, target, buy, sell, effectiveAtUtc, actorUserId);
        }
        catch (ArgumentException ex)
        {
            // Last-mile defence: the entity ctor caught something the service
            // pre-checks did not. Surface as a generic operation rejection.
            throw new UserFacingException(
                UserFacingErrorCode.OperationRejected,
                ex.Message);
        }

        try
        {
            await _repository.AddAsync(rate, ct).ConfigureAwait(false);
        }
        catch (DuplicateRateTimestampException ex)
        {
            _logger.LogInformation(ex,
                "Exchange-rate publish blocked due to duplicate timestamp. actorUserId={ActorUserId} source={Source} target={Target} effectiveAtUtc={EffectiveAtUtc:O}",
                actorUserId, source.Value, target.Value, effectiveAtUtc);

            throw new UserFacingException(
                UserFacingErrorCode.DuplicateRateTimestamp,
                ex.Message);
        }

        _logger.LogInformation(
            "AuditEvent {AuditAction} actorUserId={ActorUserId} rateId={RateId} source={Source} target={Target} buy={Buy} sell={Sell} effectiveAtUtc={EffectiveAtUtc:O}",
            MultiCurrencyAuditActions.ExchangeRateCreated, actorUserId, rate.Id,
            source.Value, target.Value, buy, sell, effectiveAtUtc);

        return rate;
    }

    public async Task<IReadOnlyList<ExchangeRate>> ListAsync(
        CurrencyCode source,
        CurrencyCode target,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);
        return await _repository.ListByPairAsync(source, target, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// FR-008 / FR-010 — admin attempted to edit an existing rate via PUT.
    /// Always blocked. Records an
    /// <see cref="MultiCurrencyAuditActions.ExchangeRateEditAttemptBlocked"/>
    /// audit entry and surfaces
    /// <see cref="UserFacingErrorCode.RateImmutableUseSupersede"/>.
    /// </summary>
    public Task RecordEditAttemptAsync(Guid rateId, string actorUserId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actorUserId);
        _logger.LogInformation(
            "AuditEvent {AuditAction} actorUserId={ActorUserId} rateId={RateId}",
            MultiCurrencyAuditActions.ExchangeRateEditAttemptBlocked, actorUserId, rateId);
        return Task.CompletedTask;
    }

    /// <summary>
    /// FR-008 / FR-010 — admin attempted to delete a rate via DELETE.
    /// Always blocked. Records an
    /// <see cref="MultiCurrencyAuditActions.ExchangeRateDeleteAttemptBlocked"/>
    /// audit entry and surfaces
    /// <see cref="UserFacingErrorCode.RateImmutableUseSupersede"/>.
    /// </summary>
    public Task RecordDeleteAttemptAsync(Guid rateId, string actorUserId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actorUserId);
        _logger.LogInformation(
            "AuditEvent {AuditAction} actorUserId={ActorUserId} rateId={RateId}",
            MultiCurrencyAuditActions.ExchangeRateDeleteAttemptBlocked, actorUserId, rateId);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Spec 015 / US3 — typed exception carrying a <see cref="UserFacingErrorCode"/>
/// plus an optional ModelState field-key for ASP.NET MVC binding. The Web layer
/// translates the code via <c>IUserFacingErrorTranslator</c>.
/// </summary>
public sealed class UserFacingException : Exception
{
    public UserFacingErrorCode Code { get; }
    public string? FieldKey { get; }

    public UserFacingException(UserFacingErrorCode code, string message, string? fieldKey = null)
        : base(message)
    {
        Code = code;
        FieldKey = fieldKey;
    }
}
