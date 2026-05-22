// Spec 021 / US5 / T126 / FR-018 — application-layer command for self-service
// profile edit. Only the four self-editable fields are accepted; Email /
// Role / Group / CodigoPersonal are NOT part of this payload (defense-in-
// depth: even if the user smuggles them in the form post, the handler
// cannot touch them).

namespace FundingPlatform.Application.Identity;

/// <summary>
/// Spec 021 / FR-018 — self-service profile update. The user owns these
/// four fields. Anything else on <c>ApplicationUser</c> is admin-managed
/// and SHOULD NOT appear on this command — the absence is by design.
/// </summary>
public sealed record UpdateProfileCommand(
    string UserId,
    string? FirstName,
    string? LastName,
    string? Phone,
    string? Address);

/// <summary>
/// Outcome of the update handler. <see cref="ErrorMessages"/> is non-empty
/// only on failure (translated es-CR strings).
/// </summary>
public sealed record UpdateProfileResult(bool Success, IReadOnlyList<string> ErrorMessages)
{
    public static UpdateProfileResult Ok() => new(true, Array.Empty<string>());
    public static UpdateProfileResult Failed(params string[] messages) => new(false, messages);
}

/// <summary>
/// Spec 021 / US5 / T126 — application-layer seam. The implementation lives
/// in the Web project because it depends on <c>UserManager&lt;ApplicationUser&gt;</c>.
/// </summary>
public interface IUpdateProfileHandler
{
    Task<UpdateProfileResult> HandleAsync(UpdateProfileCommand command, CancellationToken ct);
}
