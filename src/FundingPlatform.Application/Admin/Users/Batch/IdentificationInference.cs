using System.Text.RegularExpressions;
using FundingPlatform.Domain.Enums;
using FundingPlatform.Domain.ValueObjects;

namespace FundingPlatform.Application.Admin.Users.Batch;

/// <summary>
/// Spec 034 — infers the identification type from the raw value's structure, so a
/// batch row need not declare its type. The batch is for individuals, so the
/// inferred set is the individual-applicable types:
/// <list type="bullet">
///   <item>9 digits → <see cref="IdentificationType.CedulaFisica"/> (CR national).</item>
///   <item>11–12 digits → <see cref="IdentificationType.Dimex"/> (foreign resident).</item>
///   <item>contains a letter → <see cref="IdentificationType.Pasaporte"/>.</item>
/// </list>
/// Any other shape (e.g. a 10-digit value, which would be an entity cédula
/// jurídica / NITE — not an individual, and most likely a mistyped cédula) yields
/// no inference and the caller errors the row, so a bad value is surfaced rather
/// than silently coerced. The chosen type is validated through the existing
/// <see cref="Identification"/> value object, so the persisted value is canonical
/// and identical to what the single-create form would store.
/// </summary>
public static partial class IdentificationInference
{
    public static bool TryInfer(string? raw, out Identification? id)
    {
        id = null;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }
        var trimmed = raw.Trim();

        // A value carrying any letter can only be a passport (the numeric types
        // would strip the letters and mis-measure the length).
        if (HasLetter().IsMatch(trimmed))
        {
            return Identification.TryFrom(IdentificationType.Pasaporte, trimmed, out id);
        }

        var digitCount = NonDigit().Replace(trimmed, string.Empty).Length;
        IdentificationType? inferred = digitCount switch
        {
            9 => IdentificationType.CedulaFisica,
            11 or 12 => IdentificationType.Dimex,
            _ => null,
        };
        if (inferred is null)
        {
            return false;
        }
        return Identification.TryFrom(inferred.Value, trimmed, out id);
    }

    [GeneratedRegex(@"[A-Za-z]")]
    private static partial Regex HasLetter();

    [GeneratedRegex(@"\D")]
    private static partial Regex NonDigit();
}
