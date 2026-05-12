using System.Text;
using System.Text.Json;
using FundingPlatform.Application.Abstractions.AiComparison;
using FundingPlatform.Infrastructure.AiComparison.Redaction.Patterns;

namespace FundingPlatform.Infrastructure.AiComparison.Redaction;

/// <summary>
/// Spec 020 / FR-B2..FR-B4 / NFR-S1 — concrete PII redactor. Deterministic
/// (no salt/randomness); same input always produces same output. Throws
/// <see cref="PiiRedactionFailedException"/> on empty/whitespace-only file text
/// (signal for image-only PDFs the caller should refuse with the spec's exact
/// es-CR message).
/// </summary>
public class PiiRedactor : IPiiRedactor
{
    private const string Redaction = "[REDACTED]";

    public RedactionResult RedactStructured(SupplierAssemblyDto assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        var counts = new Dictionary<string, int>(StringComparer.Ordinal);

        string? Scrub(string? value, string fieldName)
        {
            if (string.IsNullOrWhiteSpace(value)) return value;
            counts[fieldName] = counts.GetValueOrDefault(fieldName, 0) + 1;
            return Redaction;
        }

        var safe = assembly with
        {
            OwnerDni = Scrub(assembly.OwnerDni, "supplierOwnerDni"),
            OwnerPersonalPhone = Scrub(assembly.OwnerPersonalPhone, "supplierOwnerPhone"),
            ApplicantNationalId = Scrub(assembly.ApplicantNationalId, "applicantNationalId"),
            ApplicantPersonalPhone = Scrub(assembly.ApplicantPersonalPhone, "applicantPersonalPhone"),
            ApplicantPersonalEmail = Scrub(assembly.ApplicantPersonalEmail, "applicantPersonalEmail"),
        };

        var payload = JsonSerializer.Serialize(safe);
        var spans = counts.Select(kv => new RedactedSpan(kv.Key, kv.Value)).ToList();
        return new RedactionResult(payload, spans);
    }

    public RedactionResult RedactFileText(Guid blobId, string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new PiiRedactionFailedException(blobId,
                "No se pudo procesar de forma segura el archivo: envíe un PDF con capa de texto.");
        }

        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        var working = new StringBuilder(text);

        // Run email first (most distinctive), then phone, then cédula —
        // overlapping matches are resolved by the order applied.
        var current = working.ToString();

        var (afterEmail, emailCount) = ReplaceAll(current, PiiPatterns.Email);
        if (emailCount > 0) counts["filePatternEmail"] = emailCount;

        var (afterPhone, phoneCount) = ReplaceAll(afterEmail, PiiPatterns.Phone);
        if (phoneCount > 0) counts["filePatternPhone"] = phoneCount;

        var (afterCedula, cedulaCount) = ReplaceAll(afterPhone, PiiPatterns.Cedula);
        if (cedulaCount > 0) counts["filePatternCedula"] = cedulaCount;

        var spans = counts.Select(kv => new RedactedSpan(kv.Key, kv.Value)).ToList();
        return new RedactionResult(afterCedula, spans);
    }

    private static (string output, int count) ReplaceAll(string input, System.Text.RegularExpressions.Regex pattern)
    {
        var count = 0;
        var replaced = pattern.Replace(input, _ =>
        {
            count++;
            return Redaction;
        });
        return (replaced, count);
    }
}
