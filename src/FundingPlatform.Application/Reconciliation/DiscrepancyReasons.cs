namespace FundingPlatform.Application.Reconciliation;

/// <summary>
/// Spec 048 — es-CR refusal strings for the discrepancy-lifecycle surface, produced by the
/// Infrastructure <c>DiscrepancyLifecycleService</c>. Kept in the Application layer (not
/// <c>Web.Resources</c>) because the service that produces them lives in Infrastructure and must not
/// depend on Web — the spec-045/046/047 <c>DisbursementReasons</c>/<c>EvidenceReasons</c> cross-layer
/// precedent. Each is paired with a stable <see cref="Codes"/> value so the Web layer can branch.
/// </summary>
public static class DiscrepancyReasons
{
    public static class Codes
    {
        public const string NotFound = "DISCREPANCY_NOT_FOUND";
        public const string CannotWaiveBlocking = "CANNOT_WAIVE_BLOCKING";
        public const string ReasonRequired = "REASON_REQUIRED";
        public const string AssigneeRequired = "ASSIGNEE_REQUIRED";
        public const string NotActionable = "NOT_ACTIONABLE";
        public const string Concurrency = "CONCURRENCY";
    }

    public const string NotFound = "No se encontró la diferencia indicada.";
    public const string CannotWaiveBlocking = "Una diferencia bloqueante no se puede exonerar; debe corregirse.";
    public const string ReasonRequired = "Debe indicar un motivo.";
    public const string AssigneeRequired = "Debe seleccionar la persona responsable.";
    public const string NotActionable = "La diferencia ya está resuelta o exonerada; no admite cambios de estado.";
    public const string Concurrency = "La diferencia cambió mientras se editaba. Vuelva a intentarlo.";
}
