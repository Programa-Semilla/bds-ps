namespace FundingPlatform.Application.Evidence;

/// <summary>
/// Spec 047 — es-CR refusal/validation strings for the evidence-graph + budget-line-closure
/// surface, produced by the Infrastructure <c>EvidenceService</c> and <c>BudgetLineClosureService</c>.
/// Kept in the Application layer (not <c>Web.Resources</c>) because the services that produce them
/// live in Infrastructure and must not depend on Web — the spec-034 <c>BatchUserRowReasons</c> /
/// spec-045/046 <c>DisbursementReasons</c> cross-layer precedent. Each is paired with a stable
/// <see cref="Codes"/> value so the Web layer can also branch programmatically.
/// </summary>
public static class EvidenceReasons
{
    public static class Codes
    {
        public const string NotFound = "EVIDENCE_NOT_FOUND";
        public const string ApplicationNotFound = "APPLICATION_NOT_FOUND";
        public const string NotExecuted = "APPLICATION_NOT_EXECUTED";
        public const string InvalidInput = "INVALID_INPUT";
        public const string AmountInvalid = "AMOUNT_INVALID";
        public const string NonCrc = "NON_CRC";
        public const string Concurrency = "CONCURRENCY";
        public const string UploadFailed = "UPLOAD_FAILED";
        public const string LineNotFound = "LINE_NOT_FOUND";

        // US1 — evidence graph + allocation.
        public const string Orphaned = "EVIDENCE_ORPHANED";
        public const string AllocationExceedsAmount = "ALLOCATION_EXCEEDS_AMOUNT";

        // US3 — closure gate + evidence lock.
        public const string EvidenceLocked = "EVIDENCE_LOCKED";
        public const string MissingRequiredDocuments = "MISSING_REQUIRED_DOCUMENTS";
        public const string PaymentNotValidated = "PAYMENT_NOT_VALIDATED";
        public const string LineEqualityMismatch = "LINE_EQUALITY_MISMATCH";
        public const string RequiredEvidenceNotFullyAllocated = "REQUIRED_EVIDENCE_NOT_FULLY_ALLOCATED";
        public const string AlreadyClosed = "ALREADY_CLOSED";
        public const string NotClosed = "NOT_CLOSED";
        public const string ReopenReasonRequired = "REOPEN_REASON_REQUIRED";

        // US4 — version history.
        public const string ReasonRequired = "REASON_REQUIRED";
    }

    public const string NotFound = "No se encontró el documento de evidencia.";
    public const string ApplicationNotFound = "No se encontró la solicitud.";
    public const string NotExecuted = "Solo se puede registrar evidencia en un convenio ejecutado.";
    public const string InvalidInput = "Revise los datos ingresados.";
    public const string AmountNotPositive = "El monto debe ser mayor que cero.";
    public const string NonCrcCurrency = "Solo se aceptan montos en colones (CRC) en esta etapa.";
    public const string Concurrency =
        "La evidencia fue modificada por otra persona. Vuelva a cargar la página e intente de nuevo.";
    public const string UploadFailed = "No se pudo adjuntar el documento. Intente de nuevo.";
    public const string LineNotFound = "No se encontró la línea presupuestaria en esta solicitud.";

    // US1
    public const string Orphaned =
        "La evidencia debe vincularse al menos a una línea presupuestaria o a un desembolso.";
    public const string AllocationExceedsAmount =
        "La suma asignada a las líneas no puede superar el monto del documento.";

    // US3
    public const string EvidenceLocked =
        "La línea está cerrada; la evidencia quedó bloqueada. Reabra la línea para modificarla.";
    public const string MissingRequiredDocuments =
        "No se puede cerrar la línea: faltan documentos requeridos.";
    public const string PaymentNotValidated =
        "No se puede cerrar la línea: hay pagos atribuidos que aún no han sido validados.";
    public const string LineEqualityMismatch =
        "No se puede cerrar la línea: el monto pagado no coincide con el monto aceptado.";
    public const string RequiredEvidenceNotFullyAllocated =
        "No se puede cerrar la línea: cada documento requerido debe estar totalmente asignado a la línea.";
    public const string AlreadyClosed = "La línea ya está cerrada.";
    public const string NotClosed = "La línea no está cerrada.";
    public const string ReopenReasonRequired = "Debe indicar el motivo de la reapertura.";

    // US4
    public const string ReasonRequired = "Debe indicar el motivo del cambio para registrar una nueva versión.";
}
