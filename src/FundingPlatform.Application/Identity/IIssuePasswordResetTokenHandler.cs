// Spec 021 / US5 / T126.

namespace FundingPlatform.Application.Identity;

/// <summary>
/// Spec 021 / US5 / T126 / FR-028 — Application-layer seam for issuing a
/// single-use password-reset token. The web layer drives the side effect
/// (compose link, send email) so the handler stays free of routing concerns;
/// see <c>AccountController.ForgotPassword</c>.
/// </summary>
public interface IIssuePasswordResetTokenHandler
{
    Task<IssuePasswordResetTokenResult> HandleAsync(
        IssuePasswordResetTokenCommand command,
        CancellationToken ct);
}
