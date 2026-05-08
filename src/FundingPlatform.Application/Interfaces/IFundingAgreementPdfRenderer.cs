using FundingPlatform.Application.DTOs;

namespace FundingPlatform.Application.Interfaces;

public interface IFundingAgreementPdfRenderer
{
    /// <summary>
    /// Existing surface — renders raw HTML to a PDF byte array. Kept for
    /// back-compat with the spec-005 callers that already had HTML in hand. New
    /// callers should prefer <see cref="RenderFromModelAsync"/> so the
    /// per-line conversion-metadata pre-flight runs.
    /// </summary>
    Task<byte[]> RenderAsync(string html, string? baseUrl);

    /// <summary>
    /// Spec 015 / US5 / T511 — validates the document model's quotation lines
    /// for per-line conversion metadata (every non-CRC line must carry an
    /// embedded rate snapshot) and only then renders the document.
    ///
    /// Throws
    /// <see cref="FundingPlatform.Domain.Exceptions.MissingConversionMetadataException"/>
    /// when any item has <c>Currency != "CRC"</c> AND no
    /// <c>SnapshotRateValue</c>. CRC-only or fully-snapshotted requests render
    /// exactly as today.
    ///
    /// The <paramref name="renderHtmlAsync"/> delegate is invoked only after
    /// the pre-flight succeeds, so a failed validation never spends Razor
    /// rendering work.
    /// </summary>
    Task<byte[]> RenderFromModelAsync(
        IReadOnlyList<FundingAgreementItemRowDto> items,
        Func<Task<string>> renderHtmlAsync,
        string? baseUrl = null,
        CancellationToken ct = default);
}
