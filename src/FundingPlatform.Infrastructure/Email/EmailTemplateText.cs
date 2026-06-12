using System.Text.RegularExpressions;

namespace FundingPlatform.Infrastructure.Email;

/// <summary>
/// Shared helpers for the plain-text email template factories
/// (<see cref="InvitationEmailFactory"/>, <see cref="ForgotPasswordEmailFactory"/>,
/// <see cref="StageReminderEmailFactory"/>). These templates are <c>.cshtml</c>
/// files read as plain text (NOT Razor-rendered), so Razor constructs in them are
/// not processed and must be sanitized before <c>{{token}}</c> substitution.
/// </summary>
internal static partial class EmailTemplateText
{
    [GeneratedRegex(@"@\*[\s\S]*?\*@")]
    private static partial Regex RazorCommentRegex();

    /// <summary>
    /// Removes Razor <c>@* … *@</c> comment blocks. Without this, a header comment
    /// in a plain-text-read template renders verbatim in the email body — and any
    /// <c>{{token}}</c> inside it would be substituted, leaking the real value.
    /// Leading whitespace left by a stripped header comment is trimmed.
    /// </summary>
    public static string StripRazorComments(string template) =>
        RazorCommentRegex().Replace(template, string.Empty).TrimStart();
}
