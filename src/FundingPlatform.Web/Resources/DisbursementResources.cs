using FundingPlatform.Domain.Enums;

namespace FundingPlatform.Web.Resources;

/// <summary>
/// Spec 045 — es-CR copy for the disbursement (Desembolsos) surface. Service-produced
/// refusal strings live in <c>FundingPlatform.Application.Disbursements.DisbursementReasons</c>
/// (the Infrastructure service produces them — spec-034 cross-layer precedent); this class
/// holds the view/controller copy (titles, labels, buttons, boundary-level file errors).
/// All es-CR — no English literals in views (Constitution / conventions).
/// </summary>
public static class DisbursementResources
{
    // Page + sidebar
    public const string Nav = "Desembolsos";
    public const string Title = "Desembolsos";
    public const string Subtitle = "Registre y valide los desembolsos del convenio ejecutado.";
    public const string InboxSubtitle = "Convenios ejecutados en procesos activos. Abra uno para registrar sus desembolsos.";

    // Inbox table (mirrors EvidenceInboxResources — no English/hardcoded literals in the view)
    public const string Inbox_Col_Number = "Número";
    public const string Inbox_Col_Applicant = "Solicitante";
    public const string Inbox_Col_Fund = "Fondo";
    public const string Inbox_Col_Process = "Proceso";
    public const string Inbox_Col_Executed = "Ejecutado";
    public const string Inbox_Empty = "No hay convenios ejecutados en procesos activos.";

    // Discrepancy list labels
    public const string Discrepancy_Expected = "Esperado";
    public const string Discrepancy_Actual = "Registrado";
    public const string Discrepancy_Difference = "Diferencia";

    // Balance card
    public const string Balance_Title = "Balance del participante";
    public const string Balance_Allocated = "Asignado";
    public const string Balance_Paid = "Pagado";
    public const string Balance_Validated = "Validado";
    public const string Balance_Pending = "Pendiente de validación";
    public const string Balance_Available = "Disponible";
    public const string Balance_OverDisbursed = "Sobregiro: el disponible es negativo.";

    // Record form
    public const string Record_Heading = "Registrar desembolso";
    public const string Field_PaymentDate = "Fecha de pago";
    public const string Field_Amount = "Monto (CRC)";
    public const string Field_BankTransaction = "Referencia de transacción bancaria";
    public const string Field_BankAccount = "Referencia de cuenta bancaria (opcional)";
    public const string Action_Record = "Registrar";

    // List columns + empty state
    public const string Col_Date = "Fecha";
    public const string Col_Amount = "Monto";
    public const string Col_State = "Estado";
    public const string Col_Evidence = "Evidencia";
    public const string Col_Actions = "";
    public const string Empty = "No hay desembolsos registrados todavía.";
    public const string Action_Open = "Ver detalle";
    public const string Evidence_Present = "Sí";
    public const string Evidence_Missing = "No";

    // Detail
    public const string Detail_Title = "Detalle del desembolso";
    public const string Detail_Evidence = "Documentos de respaldo";
    public const string Detail_Discrepancies = "Diferencias detectadas";
    public const string Detail_NoDiscrepancies = "Sin diferencias: los montos coinciden a la unidad.";
    public const string Detail_Back = "Volver a desembolsos";

    // Edit
    public const string Edit_Heading = "Editar desembolso";
    public const string Action_Save = "Guardar cambios";

    // Evidence forms
    public const string Evidence_Heading = "Adjuntar o reemplazar documento";
    public const string Field_Evidence_Amount = "Monto del documento (CRC)";
    public const string Field_Evidence_Reference = "Número de referencia del documento";
    public const string Field_Evidence_Date = "Fecha del documento";
    public const string Field_Evidence_File = "Archivo";
    public const string Action_AttachEvidence = "Adjuntar";
    public const string Action_ReplaceEvidence = "Reemplazar";
    public const string Action_Download = "Descargar";

    // Validate / cancel
    public const string Action_Validate = "Validar";
    public const string Action_Cancel = "Cancelar desembolso";
    public const string Confirm_CancelTitle = "Cancelar desembolso";
    public const string Confirm_CancelBody = "¿Confirma la cancelación de este desembolso? No se puede deshacer.";
    public const string Confirm_CancelLabel = "Cancelar desembolso";
    public const string Validated_Locked = "El desembolso está validado y bloqueado contra cambios.";

    // Flashes
    public const string Flash_Recorded = "Desembolso registrado.";
    public const string Flash_Edited = "Desembolso actualizado.";
    public const string Flash_EvidenceSaved = "Documento adjuntado.";
    public const string Flash_Validated = "Desembolso validado.";
    public const string Flash_Cancelled = "Desembolso cancelado.";

    // Boundary-level file errors (controller)
    public const string Error_FileRequired = "Debe seleccionar un archivo.";
    public const string Error_FileType = "Este tipo de archivo no está permitido.";
    public const string Error_InvalidInput = "Los datos ingresados no son válidos. Revise el formulario.";

    public static string StateLabel(DisbursementState state) => state switch
    {
        DisbursementState.Recorded => "Registrado",
        DisbursementState.Inconsistent => "Inconsistente",
        DisbursementState.Validated => "Validado",
        DisbursementState.Cancelled => "Cancelado",
        _ => state.ToString(),
    };

    /// <summary>Tabler badge colour for the state pill. Never the sole signal — the text
    /// label above accompanies it (FR-014).</summary>
    public static string StateBadgeClass(DisbursementState state) => state switch
    {
        DisbursementState.Recorded => "bg-blue-lt",
        DisbursementState.Inconsistent => "bg-red-lt",
        DisbursementState.Validated => "bg-green-lt",
        DisbursementState.Cancelled => "bg-secondary-lt",
        _ => "bg-secondary-lt",
    };

    public static string EvidenceKindLabel(EvidenceKind kind) => kind switch
    {
        EvidenceKind.BankReceipt => "Comprobante bancario",
        EvidenceKind.Invoice => "Factura",
        _ => kind.ToString(),
    };

    public static string ComparisonLabel(ReconciliationComparison comparison) => comparison switch
    {
        ReconciliationComparison.DisbursementVsBankReceipt => "Desembolso vs. comprobante bancario",
        ReconciliationComparison.DisbursementVsInvoice => "Desembolso vs. factura",
        ReconciliationComparison.TotalVsAllocation => "Total desembolsado vs. monto aprobado",
        ReconciliationComparison.DisbursementSplitVsTotal => "Suma de líneas vs. monto del desembolso",
        ReconciliationComparison.LinePaymentVsBudget => "Pago de la línea vs. presupuesto comprometido",
        ReconciliationComparison.EvidenceDateAnomaly => "Anomalía en la fecha del documento",
        ReconciliationComparison.PossibleDuplicatePayment => "Posible pago duplicado",
        ReconciliationComparison.GraphInvoiceAllocationDrift => "Diferencia con la factura del grafo de evidencia",
        _ => comparison.ToString(),
    };

    // Spec 048 — severity of a persisted discrepancy. Text + icon, never colour alone (FR-025).
    public static string SeverityLabel(DiscrepancySeverity severity) => severity switch
    {
        DiscrepancySeverity.Blocking => "Bloqueante",
        DiscrepancySeverity.Warning => "Advertencia",
        _ => severity.ToString(),
    };

    /// <summary>Tabler badge colour for the severity pill. Never the sole signal — the
    /// <see cref="SeverityLabel"/> text + <see cref="SeverityIcon"/> accompany it (FR-025).</summary>
    public static string SeverityBadgeClass(DiscrepancySeverity severity) => severity switch
    {
        DiscrepancySeverity.Blocking => "bg-red-lt",
        DiscrepancySeverity.Warning => "bg-yellow-lt",
        _ => "bg-secondary-lt",
    };

    /// <summary>Tabler icon name accompanying the severity badge (redundant, non-colour signal).</summary>
    public static string SeverityIcon(DiscrepancySeverity severity) => severity switch
    {
        DiscrepancySeverity.Blocking => "ti ti-alert-octagon",
        DiscrepancySeverity.Warning => "ti ti-alert-triangle",
        _ => "ti ti-info-circle",
    };

    // Spec 048 — lifecycle state of a persisted discrepancy.
    public static string DiscrepancyStateLabel(DiscrepancyState state) => state switch
    {
        DiscrepancyState.Open => "Abierta",
        DiscrepancyState.Assigned => "Asignada",
        DiscrepancyState.UnderCorrection => "En corrección",
        DiscrepancyState.Resolved => "Resuelta",
        DiscrepancyState.Waived => "Exonerada",
        _ => state.ToString(),
    };

    public static string DiscrepancyStateBadgeClass(DiscrepancyState state) => state switch
    {
        DiscrepancyState.Open => "bg-red-lt",
        DiscrepancyState.Assigned => "bg-blue-lt",
        DiscrepancyState.UnderCorrection => "bg-azure-lt",
        DiscrepancyState.Resolved => "bg-green-lt",
        DiscrepancyState.Waived => "bg-secondary-lt",
        _ => "bg-secondary-lt",
    };

    // Spec 048 — per-application discrepancy list additions.
    public const string Discrepancy_Severity = "Severidad";
    public const string Discrepancy_State = "Estado";
    public const string Discrepancy_Source = "Documento";
    public const string Discrepancy_OpenDetail = "Ver detalle de reconciliación";

    // Spec 046 — Committed dimension (6th balance figure) + per-line commit actions.
    public const string Balance_Committed = "Comprometido";
    public const string Line_Commit = "Comprometer";
    public const string Line_Uncommit = "Descomprometer";
    public const string Line_CommitState_Committed = "Comprometida";
    public const string Line_CommitState_Uncommitted = "Sin comprometer";
    public const string Flash_LineCommitted = "Línea comprometida.";
    public const string Flash_LineUncommitted = "Línea descomprometida.";

    // Spec 046 / US3 — per-line split editor on the disbursement Record/Edit form.
    public const string Split_Heading = "Distribución por línea";
    public const string Split_Line = "Línea";
    public const string Split_Amount = "Monto (CRC)";
    public const string Split_Total = "Total distribuido";
    public const string Split_MustCommitFirst = "Solo se pueden asignar pagos a líneas comprometidas.";
}
