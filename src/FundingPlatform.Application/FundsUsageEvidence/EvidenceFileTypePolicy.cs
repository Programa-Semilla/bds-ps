using System.IO;

namespace FundingPlatform.Application.FundsUsageEvidence;

/// <summary>
/// Spec 036 / D3 — pure, unit-testable allow-list for funds-usage evidence files.
/// A file is accepted iff its extension is in the allow-list AND the declared
/// content-type is consistent with that extension's family AND the buffered head
/// bytes match the family's magic signature. Mirrors the <c>%PDF-</c> boundary check
/// in <c>AdminFundsController.ValidatePdfAsync</c>, generalized to several families.
/// </summary>
public static class EvidenceFileTypePolicy
{
    /// <summary>The canonical allowed-extension list — drives the accept hint + tests.</summary>
    public static IReadOnlyList<string> AllowedExtensions { get; } =
    [
        ".pdf", ".png", ".jpg", ".jpeg", ".webp", ".heic", ".heif",
        ".docx", ".doc", ".xlsx", ".xls",
    ];

    private static readonly byte[] PdfMagic = "%PDF-"u8.ToArray();
    private static readonly byte[] PngMagic = [0x89, 0x50, 0x4E, 0x47];
    private static readonly byte[] JpegMagic = [0xFF, 0xD8, 0xFF];
    private static readonly byte[] Riff = "RIFF"u8.ToArray();
    private static readonly byte[] Webp = "WEBP"u8.ToArray();
    private static readonly byte[] Ftyp = "ftyp"u8.ToArray();
    private static readonly byte[] ZipMagic = [0x50, 0x4B, 0x03, 0x04]; // PK\x03\x04 (OOXML)
    private static readonly byte[] OleMagic = [0xD0, 0xCF, 0x11, 0xE0]; // legacy OLE compound (.doc/.xls)

    /// <summary>The minimum number of head bytes the caller should buffer for a reliable sniff.</summary>
    public const int HeadByteCount = 16;

    public static bool IsAllowed(string fileName, string? declaredContentType, ReadOnlySpan<byte> head)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return false;
        }

        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        var ct = (declaredContentType ?? string.Empty).Trim().ToLowerInvariant();

        return ext switch
        {
            ".pdf" => CtOk(ct, "application/pdf") && StartsWith(head, PdfMagic),
            ".png" => CtOk(ct, "image/png") && StartsWith(head, PngMagic),
            ".jpg" or ".jpeg" => CtOk(ct, "image/jpeg", "image/jpg") && StartsWith(head, JpegMagic),
            ".webp" => CtOk(ct, "image/webp") && IsWebp(head),
            ".heic" or ".heif" => CtOk(ct, "image/heic", "image/heif") && IsHeif(head),
            ".docx" => CtOk(ct, "application/vnd.openxmlformats-officedocument.wordprocessingml.document")
                       && StartsWith(head, ZipMagic),
            ".xlsx" => CtOk(ct, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")
                       && StartsWith(head, ZipMagic),
            ".doc" => CtOk(ct, "application/msword") && StartsWith(head, OleMagic),
            ".xls" => CtOk(ct, "application/vnd.ms-excel") && StartsWith(head, OleMagic),
            _ => false,
        };
    }

    // The declared content-type must either be empty (some browsers omit it) or
    // match one of the family's expected types. We do not reject on a missing
    // content-type because the magic-byte check is the authoritative gate.
    private static bool CtOk(string ct, params string[] expected)
        => ct.Length == 0 || Array.Exists(expected, e => string.Equals(e, ct, StringComparison.Ordinal));

    private static bool StartsWith(ReadOnlySpan<byte> head, ReadOnlySpan<byte> magic)
        => head.Length >= magic.Length && head[..magic.Length].SequenceEqual(magic);

    // RIFF....WEBP — "RIFF" at offset 0, "WEBP" at offset 8.
    private static bool IsWebp(ReadOnlySpan<byte> head)
        => head.Length >= 12 && head[..4].SequenceEqual(Riff) && head.Slice(8, 4).SequenceEqual(Webp);

    // ISO-BMFF: a "ftyp" box marker at offset 4 (the brand follows; any HEIF brand allowed).
    private static bool IsHeif(ReadOnlySpan<byte> head)
        => head.Length >= 8 && head.Slice(4, 4).SequenceEqual(Ftyp);
}
