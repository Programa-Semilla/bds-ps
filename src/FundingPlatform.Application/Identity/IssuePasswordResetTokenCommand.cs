// Spec 021 / US5 / T126 / FR-028 — application-layer command for the
// "issue a single-use password-reset token" flow. The handler composes
// ASP.NET Identity's DataProtectorTokenProvider (which is the source of
// cryptographic correctness) with the local PasswordResetTokens
// single-use marker (which adds replay protection inside the TTL).

namespace FundingPlatform.Application.Identity;

/// <summary>
/// Spec 021 / FR-028 / R-3 — request to issue a fresh password-reset token
/// for <paramref name="Email"/>. The handler:
///   1. Resolves the user by email. If unknown, returns
///      <see cref="IssuePasswordResetTokenResult.UnknownUser"/> so the
///      controller can render the same neutral response (no enumeration).
///   2. Calls <c>UserManager.GeneratePasswordResetTokenAsync</c> to get the
///      Identity-issued raw token (60-minute TTL configured globally).
///   3. Persists the SHA-256 marker via <c>IPasswordResetTokenStore</c>
///      so the second reset attempt within the TTL is rejected.
///   4. Hands the controller the raw token + user identity so the controller
///      can compose the reset link and call <c>IEmailSender</c> with the
///      rendered <c>ForgotPasswordEmail</c> template.
///
/// <para>The handler intentionally does NOT send the email itself — email
/// composition needs <c>Url.Action</c> from MVC routing (the reset link),
/// which is a controller-layer concern.</para>
/// </summary>
public sealed record IssuePasswordResetTokenCommand(string Email);

/// <summary>
/// Outcome of the issue handler.
/// </summary>
public sealed record IssuePasswordResetTokenResult(
    bool UserFound,
    string? UserId,
    string? Email,
    string? FirstName,
    string? RawToken)
{
    public static IssuePasswordResetTokenResult UnknownUser() => new(false, null, null, null, null);

    public static IssuePasswordResetTokenResult Issued(
        string userId, string email, string? firstName, string rawToken) =>
        new(true, userId, email, firstName, rawToken);
}
