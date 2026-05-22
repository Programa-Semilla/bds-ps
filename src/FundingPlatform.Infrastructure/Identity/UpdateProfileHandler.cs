// Spec 021 / US5 / T126 / FR-018.

using FundingPlatform.Application.Identity;
using FundingPlatform.Domain.Entities;
using Microsoft.AspNetCore.Identity;

namespace FundingPlatform.Infrastructure.Identity;

/// <summary>
/// Spec 021 / FR-018 — self-service profile edit. ONLY the four fields on
/// <see cref="UpdateProfileCommand"/> are mutated; Email / Role / Group /
/// CodigoPersonal are admin-managed and never touched here (defense-in-
/// depth — even if a smuggled form post arrives, those fields stay put).
/// </summary>
public sealed class UpdateProfileHandler : IUpdateProfileHandler
{
    private readonly UserManager<ApplicationUser> _userManager;

    public UpdateProfileHandler(UserManager<ApplicationUser> userManager)
    {
        _userManager = userManager;
    }

    public async Task<UpdateProfileResult> HandleAsync(
        UpdateProfileCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (string.IsNullOrWhiteSpace(command.UserId))
        {
            return UpdateProfileResult.Failed("Usuario no encontrado.");
        }

        var user = await _userManager.FindByIdAsync(command.UserId).ConfigureAwait(false);
        if (user is null)
        {
            return UpdateProfileResult.Failed("Usuario no encontrado.");
        }

        user.FirstName = Trim(command.FirstName);
        user.LastName = Trim(command.LastName);
        user.PhoneNumber = Trim(command.Phone);

        // ApplicationUser doesn't have a dedicated Address column yet — store
        // it on IdentityUser.UserName? No, UserName is the email. Use a free
        // claim or alternate column. For spec 021 / US5 / T126 we surface the
        // Address on the form and persist it via the ProfileAddress user-claim
        // since the entity has no Address column. This keeps the schema
        // unchanged (US5 carves out NO schema deltas per Phase 2a).
        await UpsertAddressClaimAsync(user, command.Address).ConfigureAwait(false);

        var result = await _userManager.UpdateAsync(user).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            return UpdateProfileResult.Failed([.. result.Errors.Select(e => e.Description)]);
        }

        return UpdateProfileResult.Ok();
    }

    private async Task UpsertAddressClaimAsync(ApplicationUser user, string? address)
    {
        const string claimType = "profile.address";

        var existing = (await _userManager.GetClaimsAsync(user).ConfigureAwait(false))
            .Where(c => c.Type == claimType)
            .ToList();

        foreach (var c in existing)
        {
            await _userManager.RemoveClaimAsync(user, c).ConfigureAwait(false);
        }

        var trimmed = Trim(address);
        if (!string.IsNullOrEmpty(trimmed))
        {
            await _userManager.AddClaimAsync(
                user, new System.Security.Claims.Claim(claimType, trimmed)).ConfigureAwait(false);
        }
    }

    private static string? Trim(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
