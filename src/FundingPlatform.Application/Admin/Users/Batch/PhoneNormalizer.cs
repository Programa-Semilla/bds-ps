using System.Text.RegularExpressions;

namespace FundingPlatform.Application.Admin.Users.Batch;

/// <summary>
/// Spec 034 / D4 / FR-005 — normalizes a raw spreadsheet phone cell. Phone is
/// optional and non-identifying, so this never rejects a row; it only cleans the
/// value: take the first number when several are listed, drop formatting, and
/// strip a leading Costa Rica country code (<c>506</c>).
/// </summary>
public static partial class PhoneNormalizer
{
    /// <summary>
    /// Returns the normalized national phone digits, or <c>null</c> when the cell
    /// is blank or carries no digits.
    /// <list type="number">
    /// <item>null/blank → null.</item>
    /// <item>Split on multi-number separators (<c>/ , ; |</c>); take the first
    /// non-empty token (a single number written with spaces, e.g. "506 8888 1111",
    /// is one token — whitespace is NOT a separator).</item>
    /// <item>Strip every non-digit from that token.</item>
    /// <item>If the result starts with <c>506</c> and is longer than 8 digits,
    /// drop the leading <c>506</c>.</item>
    /// </list>
    /// </summary>
    public static string? Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var firstToken = MultiNumberSeparators()
            .Split(raw)
            .FirstOrDefault(t => !string.IsNullOrWhiteSpace(t));
        if (firstToken is null)
        {
            return null;
        }

        var digits = NonDigits().Replace(firstToken, string.Empty);
        if (digits.Length == 0)
        {
            return null;
        }

        if (digits.Length > 8 && digits.StartsWith("506", StringComparison.Ordinal))
        {
            digits = digits[3..];
        }

        return digits.Length == 0 ? null : digits;
    }

    [GeneratedRegex(@"[\/,;|]")]
    private static partial Regex MultiNumberSeparators();

    [GeneratedRegex(@"\D")]
    private static partial Regex NonDigits();
}
