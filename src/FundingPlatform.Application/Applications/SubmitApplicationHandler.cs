// Spec 021 — see specs/021-feedback-session-may13/tasks.md T091
// and contracts/applicant-routes.md (POST /Applications/{publicCode}/Submit).

using FundingPlatform.Application.Applications.Commands;

namespace FundingPlatform.Application.Applications;

/// <summary>
/// Spec 021 / T091 / FR-006 / FR-017 — submit handler seam. The
/// Infrastructure-side implementation wraps the stage-aware
/// <c>Application.Submit(int, StageKind, DateTimeOffset, DateTimeOffset)</c>
/// overload, resolving <c>stageClosesAt</c> from
/// <see cref="Domain.Entities.Process.OverrideForStage"/> when the
/// Application's group is attached to a Process, falling back to the
/// platform default in <c>SystemConfigurations[Stage.Solicitud.WindowDays]</c>.
///
/// <para>Raises <see cref="Domain.Exceptions.StageWindowClosedException"/>
/// when the stage window has passed (mapped to HTTP 422 by the global
/// filter); throws <see cref="InvalidOperationException"/> when the
/// FR-017 predicate chain fails.</para>
/// </summary>
public interface ISubmitApplicationHandler
{
    Task SubmitAsync(SubmitApplicationCommand cmd, CancellationToken ct = default);
}
