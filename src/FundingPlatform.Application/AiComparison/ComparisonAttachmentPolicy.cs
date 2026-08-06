namespace FundingPlatform.Application.AiComparison;

/// <summary>
/// Spec 020 — decides how a supplier attachment is handed to the AI provider,
/// by sniffing magic bytes rather than trusting the stored content type or the
/// original file extension.
///
/// Before this existed the orchestrator wrapped <i>every</i> attachment in a
/// <c>PdfBlock</c>, so anything that was not a PDF went up declared as
/// <c>application/pdf</c> and the provider rejected the whole extract call with
/// "The PDF specified was not valid" — surfacing to the reviewer as the opaque
/// "Contacte un administrador". Images are a first-class quotation format
/// (phone photos of a printed quote), so they get their own block; anything the
/// provider cannot read at all is refused by <c>blobId</c> so the message can
/// name the offending file.
/// </summary>
public static class ComparisonAttachmentPolicy
{
    public const string PdfMediaType = "application/pdf";
    public const string PngMediaType = "image/png";
    public const string JpegMediaType = "image/jpeg";

    /// <summary>How a given attachment must be represented in the AI request.</summary>
    public enum AttachmentKind
    {
        /// <summary>Not a format the provider can read — refuse with <c>unsupported_format</c>.</summary>
        Unsupported = 0,
        Pdf = 1,
        Image = 2,
    }

    public readonly record struct AttachmentClassification(AttachmentKind Kind, string? MediaType);

    private static readonly byte[] PdfSignature = "%PDF-"u8.ToArray();
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly byte[] JpegSignature = [0xFF, 0xD8, 0xFF];

    /// <summary>
    /// Classifies by content, never by name. Matches the upload allow-list
    /// (<c>SystemConfigurations.AllowedFileTypes</c> = .pdf/.jpg/.jpeg/.png):
    /// legacy rows predating that narrowing — Office documents in particular —
    /// classify as <see cref="AttachmentKind.Unsupported"/>.
    /// </summary>
    public static AttachmentClassification Classify(ReadOnlySpan<byte> content)
    {
        if (StartsWith(content, PdfSignature)) return new(AttachmentKind.Pdf, PdfMediaType);
        if (StartsWith(content, PngSignature)) return new(AttachmentKind.Image, PngMediaType);
        if (StartsWith(content, JpegSignature)) return new(AttachmentKind.Image, JpegMediaType);
        return new(AttachmentKind.Unsupported, null);
    }

    private static bool StartsWith(ReadOnlySpan<byte> content, ReadOnlySpan<byte> signature) =>
        content.Length >= signature.Length && content[..signature.Length].SequenceEqual(signature);
}
