namespace FundingPlatform.Application.Notifications.Email;

/// <summary>
/// Resolves the absolute base URL used to compose email asset (logo / partner-strip)
/// and CTA links. Centralizes the request-aware precedence that the account-link
/// composition (<c>AdminUsersController.ComposeResetLink</c> / forgot-password) already
/// uses, so that email <em>images</em> resolve to the SAME host as email <em>links</em>
/// in every environment:
/// <list type="number">
///   <item>Outside Development, a configured absolute <c>Notifications:BaseUrl</c> wins.</item>
///   <item>Otherwise the live HTTP request host (scheme + host) is used.</item>
///   <item>When no request is in scope (background dispatch worker / stage-reminder
///         worker), it falls back to the configured value.</item>
/// </list>
/// Before this seam, image URLs were always built from the static config value, so a
/// stale/dev <c>Notifications:BaseUrl</c> produced broken <c>http://localhost:5000/...</c>
/// images even though the request-aware links worked.
/// </summary>
public interface IEmailBaseUrlProvider
{
    /// <summary>The resolved absolute base URL, trailing slash trimmed; never null.</summary>
    string GetBaseUrl();
}
