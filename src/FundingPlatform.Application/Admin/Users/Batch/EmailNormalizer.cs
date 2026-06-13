using System.Text.RegularExpressions;

namespace FundingPlatform.Application.Admin.Users.Batch;

/// <summary>
/// Spec 034 — extracts a single account email from a raw CSV cell. Real intake
/// spreadsheets sometimes list more than one address in the Email column (e.g.
/// "a@x.com / b@y.com"); per the requester, the first is used and the rest are
/// ignored (mirrors the first-number rule in <see cref="PhoneNormalizer"/>).
/// </summary>
public static partial class EmailNormalizer
{
    /// <summary>
    /// Returns the first address in the cell (trimmed), or empty string when the
    /// cell is blank. Addresses are separated by <c>/ , ; |</c> or whitespace —
    /// none of which can appear inside a valid email, so splitting is safe.
    /// </summary>
    public static string FirstEmail(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }
        var first = Separators()
            .Split(raw.Trim())
            .FirstOrDefault(t => !string.IsNullOrWhiteSpace(t));
        return (first ?? string.Empty).Trim();
    }

    [GeneratedRegex(@"[\/,;|\s]+")]
    private static partial Regex Separators();
}
