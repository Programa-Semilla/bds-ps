namespace FundingPlatform.Application.Disbursements;

/// <summary>
/// Spec 045 — es-CR refusal/validation strings produced by the Infrastructure
/// <c>DisbursementService</c>. Kept in the Application layer (not <c>Web.Resources</c>)
/// because the service that produces them lives in Infrastructure and must not depend
/// on Web — the spec-034 <c>BatchUserRowReasons</c> / spec-043 <c>RegulatoryFreshnessCopy</c>
/// cross-layer precedent. Each is paired with a stable <see cref="Codes"/> value so the
/// Web layer can also branch programmatically if needed.
/// </summary>
public static class DisbursementReasons
{
    public static class Codes
    {
        public const string NotFound = "DISBURSEMENT_NOT_FOUND";
        public const string NotExecuted = "APPLICATION_NOT_EXECUTED";
        public const string AmountInvalid = "AMOUNT_INVALID";
        public const string NonCrc = "NON_CRC";
        public const string InvalidInput = "INVALID_INPUT";
        public const string Locked = "DISBURSEMENT_LOCKED";
        public const string NotPreValidation = "NOT_PRE_VALIDATION";
        public const string MissingEvidence = "MISSING_EVIDENCE";
        public const string HasDiscrepancy = "HAS_DISCREPANCY";
        public const string OverAllocation = "OVER_ALLOCATION";
        public const string Concurrency = "CONCURRENCY";
        public const string EvidenceFailed = "EVIDENCE_FAILED";
    }

    public const string NotFound = "No se encontró el desembolso.";
    public const string NotExecuted = "Solo se pueden registrar desembolsos en un convenio ejecutado.";
    public const string AmountNotPositive = "El monto debe ser mayor que cero.";
    public const string NonCrcCurrency = "Solo se aceptan montos en colones (CRC) en esta etapa.";
    public const string BankTransactionRequired = "La referencia de la transacción bancaria es obligatoria.";
    public const string DocumentReferenceRequired = "El número de referencia del documento es obligatorio.";

    public const string MissingBankReceipt = "No se puede validar: falta el comprobante bancario.";
    public const string MissingInvoice = "No se puede validar: falta la factura.";
    public const string MissingBothEvidence = "No se puede validar: faltan el comprobante bancario y la factura.";
    public const string HasDiscrepancy = "No se puede validar: existen diferencias sin resolver entre los montos.";
    public const string WouldExceedAllocation = "No se puede validar: el total desembolsado superaría el monto aprobado del convenio.";

    public const string Locked = "El desembolso ya fue validado y no puede modificarse ni eliminarse.";
    public const string CannotCancel = "Solo se puede cancelar un desembolso que aún no ha sido validado.";
    public const string EvidenceFailed = "No se pudo adjuntar el documento. Intente de nuevo.";
    public const string Concurrency =
        "El desembolso fue modificado por otra persona. Vuelva a cargar la página e intente de nuevo.";
}
