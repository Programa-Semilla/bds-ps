// Spec 026 — see specs/026-input-masks/data-model.md (Identification VO) +
// contracts/identification-validation.md. Mirrors PublicCode / CurrencyCode.

using System.Text.RegularExpressions;
using FundingPlatform.Domain.Enums;

namespace FundingPlatform.Domain.ValueObjects;

/// <summary>
/// Spec 026 — owns the identification type↔shape invariant. Canonicalises a raw
/// legal-ID string against its <see cref="IdentificationType"/> (strip to
/// alphanumerics, regroup with hyphens per type) and validates the canonical
/// value against the type's regex. Shape/length only — no check-digit
/// (Out of Scope). The canonical (hyphenated where applicable) <see cref="Value"/>
/// is what gets persisted on <c>Applicant.LegalId</c> / <c>Supplier.LegalId</c>.
///
/// Authority for both the client mask catalogue and the server
/// <c>IdentificationFormatAttribute</c>, which echo / delegate to it.
/// </summary>
public sealed partial record Identification
{
    public IdentificationType Type { get; }

    /// <summary>Canonical, persisted form (see <see cref="Canonicalize"/>).</summary>
    public string Value { get; }

    public Identification(IdentificationType type, string rawValue)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rawValue);
        var canonical = Canonicalize(type, rawValue);
        if (!Validator(type).IsMatch(canonical))
        {
            throw new ArgumentException(
                $"'{rawValue}' is not a valid {type} identification.",
                nameof(rawValue));
        }
        Type = type;
        Value = canonical;
    }

    public static Identification From(IdentificationType type, string rawValue) => new(type, rawValue);

    public static bool TryFrom(IdentificationType type, string? rawValue, out Identification? id)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            id = null;
            return false;
        }
        try
        {
            id = new Identification(type, rawValue);
            return true;
        }
        catch (ArgumentException)
        {
            id = null;
            return false;
        }
    }

    /// <summary>Validation echo used by the ViewModel attribute. Empty/whitespace is invalid here (presence is a separate rule).</summary>
    public static bool IsValid(IdentificationType type, string? rawValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return false;
        }
        return Validator(type).IsMatch(Canonicalize(type, rawValue));
    }

    /// <summary>
    /// Strips to alphanumerics and regroups per type. Idempotent: feeding a
    /// canonical value back yields the same value. Numeric types only regroup
    /// when the stripped digit count matches the type's total — otherwise the
    /// raw digits flow through and fail validation.
    /// </summary>
    public static string Canonicalize(IdentificationType type, string rawValue)
    {
        if (rawValue is null)
        {
            return string.Empty;
        }

        return type switch
        {
            IdentificationType.CedulaFisica => RegroupDigits(Digits(rawValue), 1, 4, 4),
            IdentificationType.CedulaJuridica => RegroupDigits(Digits(rawValue), 1, 3, 6),
            IdentificationType.Nite => RegroupDigits(Digits(rawValue), 1, 3, 6),
            IdentificationType.Dimex => Digits(rawValue),
            IdentificationType.Pasaporte => Alnum(rawValue).ToUpperInvariant(),
            _ => Alnum(rawValue),
        };
    }

    public override string ToString() => Value;

    public static implicit operator string(Identification id) => id.Value;

    // ---- helpers ----

    private static string Digits(string s) => DigitStripper().Replace(s, string.Empty);

    private static string Alnum(string s) => NonAlnumStripper().Replace(s, string.Empty);

    /// <summary>
    /// Regroups digits into hyphen-separated groups of the given sizes when the
    /// total matches exactly; otherwise returns the digits untouched so the
    /// per-type regex rejects them.
    /// </summary>
    private static string RegroupDigits(string digits, params int[] groups)
    {
        var total = 0;
        foreach (var g in groups)
        {
            total += g;
        }
        if (digits.Length != total)
        {
            return digits;
        }

        var parts = new string[groups.Length];
        var offset = 0;
        for (var i = 0; i < groups.Length; i++)
        {
            parts[i] = digits.Substring(offset, groups[i]);
            offset += groups[i];
        }
        return string.Join('-', parts);
    }

    private static Regex Validator(IdentificationType type) => type switch
    {
        IdentificationType.CedulaFisica => CedulaFisicaRe(),
        IdentificationType.CedulaJuridica => JuridicaRe(),
        IdentificationType.Nite => JuridicaRe(),
        IdentificationType.Dimex => DimexRe(),
        IdentificationType.Pasaporte => PasaporteRe(),
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown identification type."),
    };

    [GeneratedRegex(@"\D", RegexOptions.CultureInvariant)]
    private static partial Regex DigitStripper();

    [GeneratedRegex(@"[^A-Za-z0-9]", RegexOptions.CultureInvariant)]
    private static partial Regex NonAlnumStripper();

    [GeneratedRegex(@"^\d-\d{4}-\d{4}$", RegexOptions.CultureInvariant)]
    private static partial Regex CedulaFisicaRe();

    [GeneratedRegex(@"^\d-\d{3}-\d{6}$", RegexOptions.CultureInvariant)]
    private static partial Regex JuridicaRe();

    [GeneratedRegex(@"^\d{11,12}$", RegexOptions.CultureInvariant)]
    private static partial Regex DimexRe();

    [GeneratedRegex(@"^[A-Z0-9]{1,20}$", RegexOptions.CultureInvariant)]
    private static partial Regex PasaporteRe();
}
