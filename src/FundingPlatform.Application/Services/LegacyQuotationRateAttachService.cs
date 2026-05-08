using FundingPlatform.Application.DTOs;
using FundingPlatform.Application.Interfaces;
using FundingPlatform.Domain.Entities;
using FundingPlatform.Domain.Enums;
using FundingPlatform.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace FundingPlatform.Application.Services;

/// <summary>
/// Spec 015 / US6 / T610 — administrator surface for clearing legacy
/// <c>LegacyNeedsReview</c> flags by attaching a historical exchange rate to a
/// pre-spec-015 quotation. Pre-existing CRC rows are auto-stamped by the
/// post-deploy migration; non-CRC rows lacking a snapshot get flagged and
/// quarantined out of cross-currency totals (<see cref="ApplicationCurrencyTotal"/>)
/// until an administrator picks the historical rate that should have applied.
///
/// <para>The service mirrors the audit pattern used by
/// <see cref="CurrencyConfigService"/> and <see cref="ExchangeRateService"/>:
/// structured <see cref="ILogger"/> entries carrying the
/// <see cref="MultiCurrencyAuditActions.QuotationLegacyRateAttached"/> constant.</para>
/// </summary>
public class LegacyQuotationRateAttachService : ILegacyQuotationRateAttachService
{
    private readonly IQuotationLegacyRepository _quotations;
    private readonly IExchangeRateRepository _rates;
    private readonly ILogger<LegacyQuotationRateAttachService> _logger;

    public LegacyQuotationRateAttachService(
        IQuotationLegacyRepository quotations,
        IExchangeRateRepository rates,
        ILogger<LegacyQuotationRateAttachService> logger)
    {
        _quotations = quotations;
        _rates = rates;
        _logger = logger;
    }

    /// <summary>
    /// Returns flagged quotations along with the display data the admin needs to
    /// pick a historical rate. Rows are ordered by oldest CreatedAt first so the
    /// admin works through the legacy backlog in submission order.
    /// </summary>
    public async Task<IReadOnlyList<LegacyQuotationDto>> ListAsync(CancellationToken ct = default)
    {
        var rows = await _quotations.ListFlaggedAsync(ct).ConfigureAwait(false);
        return rows.Select(r => new LegacyQuotationDto(
                r.QuotationId,
                r.ApplicationId,
                r.ItemId,
                r.ItemName,
                r.SupplierName,
                r.Price,
                r.Currency,
                r.CreatedAt))
            .ToList();
    }

    /// <summary>
    /// Attaches a historical rate to a flagged quotation. Re-attaching an
    /// already-attached (non-flagged) quotation throws
    /// <see cref="InvalidOperationException"/> rather than silently overwriting
    /// the existing snapshot — once a snapshot is on file it is the
    /// system-of-record value (FR-013, FR-016) and changing it requires a full
    /// rate-change workflow, not the legacy-attach path.
    /// </summary>
    public async Task AttachAsync(
        int quotationId,
        Guid rateId,
        string actorUserId,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actorUserId);

        var quotation = await _quotations.GetByIdAsync(quotationId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Quotation {quotationId} not found.");

        if (!quotation.LegacyNeedsReview)
        {
            throw new InvalidOperationException(
                $"Quotation {quotationId} is not flagged for legacy review; refusing to overwrite an existing snapshot.");
        }

        var rate = await _rates.GetByIdAsync(rateId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Exchange rate {rateId} not found.");

        // MVP: Buy is the canonical conversion direction (USD -> CRC). The
        // Quotation row is non-CRC by virtue of the legacy-flag predicate above,
        // so we always apply Buy here.
        var snapshot = rate.ToSnapshot(RateType.Buy);
        var convertedCrc = rate.ConvertUsdToCrc(quotation.Price);

        quotation.AttachLegacyRate(snapshot, convertedCrc);
        rate.MarkUsed();

        await _quotations.SaveChangesAsync(ct).ConfigureAwait(false);

        _logger.LogInformation(
            "AuditEvent {AuditAction} actorUserId={ActorUserId} quotationId={QuotationId} rateId={RateId} convertedCrc={ConvertedCrc}",
            MultiCurrencyAuditActions.QuotationLegacyRateAttached,
            actorUserId,
            quotationId,
            rateId,
            convertedCrc);
    }
}
