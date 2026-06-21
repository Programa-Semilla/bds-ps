namespace FundingPlatform.Application.Notifications.Email;

/// <summary>
/// Spec 041 / Decision 1 / T003 — renders a Razor email view to a string, off the
/// HTTP request thread. Generalized out of the private view-to-string logic in
/// the Web <c>RazorEmailRenderer</c> so BOTH render paths share it:
/// <list type="bullet">
///   <item>the outbox renderer (<c>RazorEmailRenderer</c>) for the ~20 lifecycle emails, and</item>
///   <item>the direct-send factories + notifiers (identity / stage / supplier / company)</item>
/// </list>
/// route every in-scope email through the same branded <c>_EmailLayout</c> — the
/// single source of truth for brand chrome.
///
/// <para>The interface lives in Application so the Infrastructure factories can
/// depend on it; the implementation lives in Web (it needs the Razor view engine).</para>
/// </summary>
public interface IEmailViewRenderer
{
    /// <summary>
    /// Renders the Razor view at <paramref name="viewPath"/> (e.g.
    /// <c>~/Views/Emails/Identity/PasswordChangedEmail.cshtml</c>) with
    /// <paramref name="model"/>. When <paramref name="disableLayout"/> is true the
    /// shared <c>_EmailLayout</c> is skipped (plain-text <c>.text.cshtml</c> twins).
    /// </summary>
    Task<string> RenderViewAsync(
        string viewPath, object model, bool disableLayout, CancellationToken ct);
}
