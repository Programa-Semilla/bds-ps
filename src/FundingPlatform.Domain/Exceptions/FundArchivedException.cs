// Spec 029 — see specs/029-fund-entity/data-model.md (Application freeze overlay)
// and research D6 (force-freeze).

namespace FundingPlatform.Domain.Exceptions;

/// <summary>
/// Spec 029 / FR-021 — raised when a mutating action targets an application
/// whose governing Fund is Archived. The freeze overlay is orthogonal to the
/// Draft→Submitted→… state machine: an archived Fund makes every anchored
/// application read-only for non-admins without changing its persisted State.
///
/// The Web layer maps this to an es-CR error toast (no state change); the
/// controller boundary also short-circuits earlier as defense-in-depth (D6).
/// </summary>
public sealed class FundArchivedException : Exception
{
    public string ErrorCode { get; } = "FUND_ARCHIVED";

    public FundArchivedException()
        : base("El fondo que rige esta postulación está archivado. No se permiten cambios.")
    {
    }
}
