// Spec 021 — see specs/021-feedback-session-may13/data-model.md (Process stage windows).

namespace FundingPlatform.Domain.Enums;

/// <summary>
/// Spec 021 / FR-006 — the three stage windows whose duration is configured
/// as a platform default in <c>SystemConfiguration</c> and optionally overridden
/// per <see cref="FundingPlatform.Domain.Entities.Process"/>.
/// </summary>
public enum StageKind
{
    Solicitud = 0,
    Revision = 1,
    Facturacion = 2,
}
