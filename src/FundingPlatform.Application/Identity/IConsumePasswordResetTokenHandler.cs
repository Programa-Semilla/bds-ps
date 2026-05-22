// Spec 021 / US5 / T126.

namespace FundingPlatform.Application.Identity;

/// <summary>
/// Spec 021 / US5 / T126 / FR-028 — application-layer seam for consuming a
/// single-use password-reset token. The implementation lives in the Web
/// project (it depends on <c>UserManager&lt;ApplicationUser&gt;</c>); the
/// interface is here so controllers depend on the Application surface.
/// </summary>
public interface IConsumePasswordResetTokenHandler
{
    Task<ConsumePasswordResetTokenResult> HandleAsync(
        ConsumePasswordResetTokenCommand command,
        CancellationToken ct);
}
