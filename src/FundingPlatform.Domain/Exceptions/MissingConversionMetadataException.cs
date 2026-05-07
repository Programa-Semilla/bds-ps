namespace FundingPlatform.Domain.Exceptions;

/// <summary>
/// Spec 015 / US5 / FR-027 — raised by the funding-agreement PDF renderer when
/// any quotation included in the document carries a non-CRC currency without an
/// embedded <see cref="FundingPlatform.Domain.ValueObjects.ExchangeRateSnapshot"/>.
///
/// The legally-meaningful PDF must always show a CRC amount alongside any
/// non-CRC line. Without a snapshot the renderer cannot guarantee a stable
/// converted value, so it refuses rather than emitting a document with a hole.
///
/// The Web layer (<c>FundingAgreementController</c>) catches this exception,
/// logs the offending quotation ids, and re-renders the agreement view with an
/// inline Spanish error so the operator can request that an admin attach a
/// historical rate (US6) before retrying.
/// </summary>
public sealed class MissingConversionMetadataException : Exception
{
    public string ErrorCode { get; } = "MISSING_CONVERSION_METADATA";

    /// <summary>
    /// Quotation ids whose <c>Currency != 'CRC'</c> AND <c>Snapshot is null</c>.
    /// Surfaced into the structured log entry written by the controller so an
    /// admin can locate the legacy rows quickly.
    /// </summary>
    public IReadOnlyList<int> OffendingQuotationIds { get; }

    public MissingConversionMetadataException(IReadOnlyList<int> offendingQuotationIds)
        : base(BuildMessage(offendingQuotationIds))
    {
        OffendingQuotationIds = offendingQuotationIds ?? Array.Empty<int>();
    }

    private static string BuildMessage(IReadOnlyList<int>? ids)
    {
        var rendered = ids is null || ids.Count == 0
            ? "(none)"
            : string.Join(",", ids);
        return $"Cannot render funding agreement PDF: one or more non-CRC quotations have no exchange-rate snapshot. Offending quotation ids: {rendered}.";
    }
}
