// Spec 021 / US5 / T126 / FR-028 — application-layer command for the
// "consume single-use password-reset token" flow. Two checks must succeed:
//
//   1. ASP.NET Identity's UserManager.ResetPasswordAsync verifies the
//      cryptographic token + (re)sets the password. Identity tokens are
//      replayable inside the TTL by design.
//   2. IPasswordResetTokenStore.ConsumeAsync atomically flips the local
//      single-use marker row. A second attempt (replay) hits 0 rows and
//      returns false, so the reset is rejected even if the Identity token
//      is still inside its 60-minute TTL.
//
// The handler is intentionally narrow: it does not perform sign-in / refresh
// (the user is anonymous on /Account/ResetPassword); the controller redirects
// to /Account/Login with a success toast on success.

namespace FundingPlatform.Application.Identity;

/// <summary>
/// Spec 021 / FR-028 / R-3 — request payload for
/// <see cref="IConsumePasswordResetTokenHandler"/>.
/// </summary>
public sealed record ConsumePasswordResetTokenCommand(
    string UserId,
    string RawToken,
    string NewPassword);

/// <summary>
/// Outcome of the consume handler. <see cref="Success"/> is true only when
/// BOTH the local single-use marker consumed AND Identity's
/// <c>ResetPasswordAsync</c> succeeded. <see cref="ErrorMessages"/> carries
/// translated user-facing strings (es-CR) to display on the form on failure.
/// </summary>
public sealed record ConsumePasswordResetTokenResult(
    bool Success,
    IReadOnlyList<string> ErrorMessages)
{
    public static ConsumePasswordResetTokenResult Ok() => new(true, Array.Empty<string>());

    public static ConsumePasswordResetTokenResult Failed(params string[] messages) =>
        new(false, messages);
}
