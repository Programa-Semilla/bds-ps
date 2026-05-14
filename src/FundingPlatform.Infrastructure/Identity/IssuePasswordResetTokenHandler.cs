// Spec 021 / US5 / T126 / FR-028.

using FundingPlatform.Application.Abstractions;
using FundingPlatform.Application.Identity;
using FundingPlatform.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;

namespace FundingPlatform.Infrastructure.Identity;

/// <summary>
/// Spec 021 / FR-028 / R-3 — composes ASP.NET Identity's
/// <see cref="UserManager{TUser}.GeneratePasswordResetTokenAsync"/> (cryptographic
/// 60-minute token via <c>DataProtectorTokenProvider</c>) with the local
/// <see cref="IPasswordResetTokenStore"/> single-use marker so a token can
/// be replayed at most once within its TTL.
///
/// <para>Returns the raw token to the caller — the controller composes the
/// reset link with <c>Url.Action</c> and dispatches the email. No email is
/// sent for unknown users (no enumeration; FR-028).</para>
/// </summary>
public sealed class IssuePasswordResetTokenHandler : IIssuePasswordResetTokenHandler
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IPasswordResetTokenStore _tokenStore;
    private readonly ILogger<IssuePasswordResetTokenHandler> _logger;

    public IssuePasswordResetTokenHandler(
        UserManager<ApplicationUser> userManager,
        IPasswordResetTokenStore tokenStore,
        ILogger<IssuePasswordResetTokenHandler> logger)
    {
        _userManager = userManager;
        _tokenStore = tokenStore;
        _logger = logger;
    }

    public async Task<IssuePasswordResetTokenResult> HandleAsync(
        IssuePasswordResetTokenCommand command,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (string.IsNullOrWhiteSpace(command.Email))
        {
            return IssuePasswordResetTokenResult.UnknownUser();
        }

        var user = await _userManager.FindByEmailAsync(command.Email).ConfigureAwait(false);
        if (user is null)
        {
            // FR-028 — return the same neutral outcome whether or not the email
            // is on file. The controller renders the identical neutral view in
            // both branches.
            _logger.LogInformation(
                "Password reset requested for unknown email; returning neutral response (no enumeration).");
            return IssuePasswordResetTokenResult.UnknownUser();
        }

        var rawToken = await _userManager.GeneratePasswordResetTokenAsync(user).ConfigureAwait(false);

        // Persist the SHA-256 marker so a replay within the TTL is rejected.
        await _tokenStore.IssueAsync(user.Id, rawToken, PasswordResetToken.DefaultLifetime, ct)
            .ConfigureAwait(false);

        return IssuePasswordResetTokenResult.Issued(user.Id, user.Email!, user.FirstName, rawToken);
    }
}
