using FundingPlatform.Application.DTOs;

namespace FundingPlatform.Application.Interfaces;

/// <summary>
/// Spec 015 / US6 — administrator surface for clearing the
/// <c>LegacyNeedsReview</c> flag on pre-spec-015 quotations by attaching a
/// historical exchange rate. The two operations the admin UI needs:
///   - List the flagged queue with enough display data to pick a rate.
///   - Attach a chosen rate to a flagged quotation, clearing the flag.
/// </summary>
public interface ILegacyQuotationRateAttachService
{
    Task<IReadOnlyList<LegacyQuotationDto>> ListAsync(CancellationToken ct = default);

    Task AttachAsync(int quotationId, Guid rateId, string actorUserId, CancellationToken ct = default);
}
