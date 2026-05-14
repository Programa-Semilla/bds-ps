// Spec 021 — see specs/021-feedback-session-may13/data-model.md (PublicCode VO) + research.md R-1.

using System.Text.RegularExpressions;

namespace FundingPlatform.Domain.ValueObjects;

/// <summary>
/// Spec 021 / FR-008 — opaque per-Application identifier displayed everywhere the
/// Application's identity surfaces (dashboard, reviewer queue, signing inbox,
/// Funding Agreement PDF, notification emails). 8 alphanumeric characters split
/// by a hyphen across the base32 alphabet <c>[A-HJ-NP-Z2-9]</c> (0/O/1/I/L
/// excluded to avoid dictation ambiguity). Internal numeric <c>Id</c> remains
/// the primary key — the public code is for human reference only.
///
/// This value object validates the shape on construction. Generation with
/// DB-side UNIQUE collision retry lives in <c>IPublicCodeGenerator</c>
/// (Infrastructure) per R-1's 3-attempt budget.
/// </summary>
public sealed partial record PublicCode
{
    /// <summary>Regex source of truth — also stamped onto the DB CHECK constraint.</summary>
    public const string Pattern = "^[A-HJ-NP-Z2-9]{4}-[A-HJ-NP-Z2-9]{4}$";

    /// <summary>Total rendered length including the separator (4 + 1 + 4).</summary>
    public const int Length = 9;

    private static readonly Regex _validator = BuildValidator();

    public string Value { get; }

    public PublicCode(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var canonical = value.Trim().ToUpperInvariant();
        if (!_validator.IsMatch(canonical))
        {
            throw new ArgumentException(
                $"PublicCode must match {Pattern} (received: '{value}').",
                nameof(value));
        }
        Value = canonical;
    }

    public static PublicCode Parse(string value) => new(value);

    public static bool TryParse(string? value, out PublicCode? code)
    {
        if (value is null)
        {
            code = null;
            return false;
        }
        try
        {
            code = new PublicCode(value);
            return true;
        }
        catch (ArgumentException)
        {
            code = null;
            return false;
        }
    }

    public override string ToString() => Value;

    public static implicit operator string(PublicCode code) => code.Value;

    [GeneratedRegex(Pattern, RegexOptions.CultureInvariant)]
    private static partial Regex BuildValidator();
}
