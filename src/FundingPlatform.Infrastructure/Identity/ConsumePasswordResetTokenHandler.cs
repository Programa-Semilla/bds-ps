// Spec 021 / US5 / T126 / FR-028.

using FundingPlatform.Application.Abstractions;
using FundingPlatform.Application.Identity;
using FundingPlatform.Domain.Entities;
using FundingPlatform.Infrastructure.Email;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;

namespace FundingPlatform.Infrastructure.Identity;

/// <summary>
/// Spec 021 / FR-028 / R-3 — both checks must succeed: the local single-use
/// marker (rejects replay) AND ASP.NET Identity's cryptographic token
/// validation (rejects forged / tampered tokens). On success the password
/// is reset and the security stamp is refreshed; the controller then
/// redirects to <c>/Account/Login</c>.
///
/// <para>Order of operations is intentional: we ConsumeAsync FIRST. If
/// Identity's reset then fails (e.g. weak password), the marker is already
/// flipped — the user must request a new link. This is the spec-aligned
/// choice (FR-028 single-use is the stronger invariant; weak-password
/// retry is acceptable friction). Reordering would let a replay slip past
/// the single-use gate on a weak-password failure.</para>
/// </summary>
public sealed class ConsumePasswordResetTokenHandler : IConsumePasswordResetTokenHandler
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IPasswordResetTokenStore _tokenStore;
    private readonly PasswordChangedEmailFactory _passwordChangedEmailFactory;
    private readonly IEmailSender _emailSender;
    private readonly ILogger<ConsumePasswordResetTokenHandler> _logger;

    public ConsumePasswordResetTokenHandler(
        UserManager<ApplicationUser> userManager,
        IPasswordResetTokenStore tokenStore,
        PasswordChangedEmailFactory passwordChangedEmailFactory,
        IEmailSender emailSender,
        ILogger<ConsumePasswordResetTokenHandler> logger)
    {
        _userManager = userManager;
        _tokenStore = tokenStore;
        _passwordChangedEmailFactory = passwordChangedEmailFactory;
        _emailSender = emailSender;
        _logger = logger;
    }

    public async Task<ConsumePasswordResetTokenResult> HandleAsync(
        ConsumePasswordResetTokenCommand command,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (string.IsNullOrWhiteSpace(command.UserId)
            || string.IsNullOrWhiteSpace(command.RawToken)
            || string.IsNullOrWhiteSpace(command.NewPassword))
        {
            return ConsumePasswordResetTokenResult.Failed("Enlace inválido o expirado. Solicite uno nuevo.");
        }

        var user = await _userManager.FindByIdAsync(command.UserId).ConfigureAwait(false);
        if (user is null)
        {
            _logger.LogInformation(
                "Password reset attempted for unknown user id; rejecting.");
            return ConsumePasswordResetTokenResult.Failed("Enlace inválido o expirado. Solicite uno nuevo.");
        }

        // Single-use marker first: a replay within the TTL hits 0 rows and is rejected.
        var consumed = await _tokenStore.ConsumeAsync(command.UserId, command.RawToken, ct)
            .ConfigureAwait(false);
        if (!consumed)
        {
            return ConsumePasswordResetTokenResult.Failed("Enlace inválido o expirado. Solicite uno nuevo.");
        }

        // Identity-side cryptographic validation + actual password reset.
        var result = await _userManager.ResetPasswordAsync(user, command.RawToken, command.NewPassword)
            .ConfigureAwait(false);
        if (!result.Succeeded)
        {
            return ConsumePasswordResetTokenResult.Failed(
                [.. result.Errors.Select(e => e.Description)]);
        }

        // Refresh the security stamp so any outstanding cookies / sessions
        // for this user are invalidated on the next request.
        user.MustChangePassword = false;
        await _userManager.UpdateAsync(user).ConfigureAwait(false);
        await _userManager.UpdateSecurityStampAsync(user).ConfigureAwait(false);

        // Spec 041 / US3 / FR-012 — best-effort password-changed confirmation
        // (covers forgot-password AND spec-033 invite first-set; research D4). A
        // render/transport failure must never fail the password reset itself.
        await TrySendPasswordChangedConfirmationAsync(user, ct).ConfigureAwait(false);

        return ConsumePasswordResetTokenResult.Ok();
    }

    private async Task TrySendPasswordChangedConfirmationAsync(ApplicationUser user, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(user.Email))
            return;
        try
        {
            var envelope = await _passwordChangedEmailFactory
                .BuildAsync(user.Email, user.FirstName, ct).ConfigureAwait(false);
            await _emailSender.SendAsync(envelope, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to send password-changed confirmation after a password reset; the reset itself succeeded.");
        }
    }
}
