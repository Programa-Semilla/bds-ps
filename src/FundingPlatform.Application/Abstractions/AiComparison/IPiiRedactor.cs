namespace FundingPlatform.Application.Abstractions.AiComparison;

/// <summary>
/// Spec 020 / FR-B2..FR-B4 / NFR-S1 — PII redaction is the security boundary
/// for AI egress. The orchestrator MUST be the only path that constructs
/// outbound AI input bytes and it MUST route everything through this seam.
///
/// Determinism: identical input ⇒ identical output (no per-call salt).
/// </summary>
public interface IPiiRedactor
{
    /// <summary>
    /// Redact the 5 enumerated PII fields on a per-supplier structured assembly
    /// (applicant national id / phone / email, supplier-owner DNI / phone).
    /// </summary>
    RedactionResult RedactStructured(SupplierAssemblyDto assembly);

    /// <summary>
    /// Pattern-based redaction of file text (cédula, CR phone, email).
    /// Throws <see cref="PiiRedactionFailedException"/> when the supplied text
    /// is empty/whitespace-only (signal for image-only PDFs).
    /// </summary>
    RedactionResult RedactFileText(Guid blobId, string text);
}

/// <summary>
/// Structured carrier of one supplier's assembled state before redaction.
/// Body is opaque to the redactor; the field-level PII members are scrubbed.
/// </summary>
public sealed record SupplierAssemblyDto(
    Guid SupplierId,
    string SupplierName,
    string? OwnerDni,
    string? OwnerPersonalPhone,
    string? ApplicantNationalId,
    string? ApplicantPersonalPhone,
    string? ApplicantPersonalEmail,
    object Body);

public sealed record RedactionResult(
    string SafePayload,
    IReadOnlyList<RedactedSpan> RedactedSpans);

public sealed record RedactedSpan(string FieldOrPatternName, int Count);

/// <summary>
/// Thrown when redaction cannot run (e.g. image-only PDF surfaced as empty
/// text). The orchestrator converts this into the
/// `pii_redaction_failed` failure reason.
/// </summary>
public sealed class PiiRedactionFailedException : Exception
{
    public Guid BlobId { get; }
    public PiiRedactionFailedException(Guid blobId, string message)
        : base(message)
    {
        BlobId = blobId;
    }
}
