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
        _ => comparison.ToString(),
    };
}
