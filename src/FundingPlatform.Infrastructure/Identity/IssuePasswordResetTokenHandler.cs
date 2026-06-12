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

        // Spec 033 / FR-007 — resend supersedes: drop the user's prior unused
        // links before issuing so only the newest invitation is valid. Scoped
        // to the invite path via the flag; forgot-password leaves it false.
        if (command.InvalidatePriorUnused)
        {
            await _tokenStore.InvalidateUnusedAsync(user.Id, ct).ConfigureAwait(false);
        }

        var rawToken = await _userManager.GeneratePasswordResetTokenAsync(user).ConfigureAwait(false);

        // Persist the SHA-256 marker so a replay within the TTL is rejected.
        // Spec 033 — the invite passes a 72h TTL; forgot-password defaults to 60 min.
        await _tokenStore.IssueAsync(user.Id, rawToken, command.Ttl ?? PasswordResetToken.DefaultLifetime, ct)
            .ConfigureAwait(false);

        return IssuePasswordResetTokenResult.Issued(user.Id, user.Email!, user.FirstName, rawToken);
    }
}
