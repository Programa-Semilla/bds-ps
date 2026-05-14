// Spec 021 — see specs/021-feedback-session-may13/contracts/admin-routes.md (Plantilla Detach).

namespace FundingPlatform.Application.Plantillas;

/// <summary>
/// Spec 021 / FR-004 — raised when a non-force detach attempts to remove a
/// <c>ProcessPlantilla</c> snapshot that still has Active Applications relying
/// on it. The caller may retry with <c>Force = true</c> + a non-empty reason
/// (audited as <c>PlantillaForceDetached</c>).
/// </summary>
public sealed class PlantillaDetachBlockedException : Exception
{
    public int PlantillaId { get; }
    public int ProcessId { get; }
    public int ActiveApplicationCount { get; }

    public PlantillaDetachBlockedException(int plantillaId, int processId, int activeApplicationCount)
        : base($"Plantilla {plantillaId} is in use by {activeApplicationCount} active Application(s) on Process {processId}; force-detach required.")
    {
        PlantillaId = plantillaId;
        ProcessId = processId;
        ActiveApplicationCount = activeApplicationCount;
    }
}
