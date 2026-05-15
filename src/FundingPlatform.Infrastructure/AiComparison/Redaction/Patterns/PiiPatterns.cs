using System.Text.RegularExpressions;

namespace FundingPlatform.Infrastructure.AiComparison.Redaction.Patterns;

/// <summary>
/// Spec 020 / FR-B2 — PII pattern catalog. CR national-ID (cédula), CR phone,
/// and a permissive email pattern. Compiled with IgnoreCase + CultureInvariant
/// so the redactor stays deterministic across hosts.
/// </summary>
internal static class PiiPatterns
{
    /// <summary>
    /// CR cédula: standard 9-digit format `1-2345-6789` or `123456789`. We
    /// match anything that looks like 9 consecutive digits with optional
    /// dashes; some legacy stamps use a leading zero so we accept 9 OR 10
    /// digits.
    /// </summary>
    public static readonly Regex Cedula = new(
        @"\b\d{1,2}-?\d{3,4}-?\d{4,6}\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// CR phone — 8 digits, optionally with leading +506 or 506 prefix.
    /// Accepts `+506 8888-8888`, `50688888888`, `8888-8888`.
    /// </summary>
    public static readonly Regex Phone = new(
        @"\b(?:\+?506[\s-]?)?\d{4}[\s-]?\d{4}\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>Permissive email — local@domain.tld.</summary>
    public static readonly Regex Email = new(
        @"\b[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
}
