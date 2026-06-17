using System.Globalization;
using System.Text;

namespace FundingPlatform.Application.Admin.Companies;

/// <summary>
/// Spec 037 / D3 — accent + case-insensitive normalization for the app-level
/// per-applicant active-name uniqueness pre-check. Mirrors the spec-031 searchable
/// dropdown matching: NFD decompose → strip combining marks → lower-case (es-CR).
/// The DB filtered unique index is the race backstop (case-insensitive via collation);
/// this provides the accent-insensitivity + the friendly es-CR duplicate message.
/// </summary>
public static class CompanyNameNormalizer
{
    public static string Normalize(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return string.Empty;
        }

        var trimmed = name.Trim();
        var decomposed = trimmed.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var ch in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
            {
                sb.Append(ch);
            }
        }

        return sb.ToString()
            .Normalize(NormalizationForm.FormC)
            .ToLower(CultureInfo.GetCultureInfo("es-CR"));
    }

    /// <summary>True when two names are the same active-company name under es-CR accent/case folding.</summary>
    public static bool AreEquivalent(string? a, string? b)
        => string.Equals(Normalize(a), Normalize(b), StringComparison.Ordinal);
}
