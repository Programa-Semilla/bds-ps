using FundingPlatform.Domain.Enums;

namespace FundingPlatform.Web.Resources;

/// <summary>
/// Spec 047 — es-CR view/controller copy for the evidence-graph surface
/// (<c>/Applications/{id}/Evidence</c>): the evidence list, attach/allocate/replace forms, the
/// version-history panel, per-line completeness, and the budget-line closure actions. Service-
/// produced refusal strings live in <c>EvidenceReasons</c> (Application) so the Infrastructure
/// service does not depend on Web; this holds the labels/buttons/titles + the evidence-type
/// label/badge switch helpers (mirroring <c>TrancheResources</c>). All es-CR — no English literals
/// in views (Constitution / conventions).
/// </summary>
public static class EvidenceResources
{
    // Evidence list + detail
    public const string Title = "Evidencia";
    public const string Subtitle =
        "Documentos de respaldo vinculados a las líneas del presupuesto, con historial de versiones.";
    public const string Empty = "Esta solicitud aún no tiene documentos de evidencia.";
    public const string Action_Attach = "Adjuntar evidencia";
    public const string Action_Detail = "Ver detalle";
    public const string Action_Download = "Descargar";
    public const string Action_Replace = "Reemplazar";
    public const string Action_Allocate = "Asignar a líneas";
    public const string Action_Delete = "Eliminar";
    public const string Confirm_DeleteTitle = "Eliminar evidencia";
    public const string Confirm_DeleteBody =
        "¿Confirma la eliminación de este documento de evidencia? Esta acción no se puede deshacer.";
    public const string Confirm_DeleteLabel = "Eliminar";

    // Columns
    public const string Col_Type = "Tipo";
    public const string Col_Amount = "Monto";
    public const string Col_Reference = "Referencia";
    public const string Col_Date = "Fecha";
    public const string Col_Supplier = "Proveedor";
    public const string Col_Lines = "Líneas asignadas";
    public const string Col_UploadedBy = "Cargado por";
    public const string Col_Version = "Versión";

    // Attach / edit form fields
    public const string Field_Type = "Tipo de documento";
    public const string Field_Amount = "Monto (CRC)";
    public const string Field_Reference = "Número de referencia";
    public const string Field_DocumentDate = "Fecha del documento";
    public const string Field_Supplier = "Proveedor (opcional)";
    public const string Field_Disbursement = "Desembolso asociado (opcional)";
    public const string Field_File = "Archivo";
    public const string Field_Reason = "Motivo del cambio";
    public const string Field_ReasonHint =
        "Explique brevemente por qué reemplaza el archivo o edita los montos (queda en el historial).";

    // Allocation editor
    public const string Alloc_Heading = "Asignación por línea";
    public const string Alloc_Subtitle =
        "Distribuya el monto del documento entre las líneas del presupuesto. La suma no puede superar el monto del documento.";
    public const string Alloc_Line = "Línea";
    public const string Alloc_Amount = "Monto asignado";
    public const string Alloc_Total = "Total asignado";
    public const string Alloc_None = "Sin asignar";

    // Version history
    public const string History_Title = "Historial de versiones";
    public const string History_Current = "Versión actual";
    public const string History_Superseded = "Reemplazada";
    public const string History_By = "Por";
    public const string History_At = "Fecha";
    public const string History_Reason = "Motivo";
    public const string History_Hash = "Huella (SHA-256)";

    // Flashes
    public const string Flash_Attached = "Evidencia adjuntada.";
    public const string Flash_Replaced = "Nueva versión de la evidencia registrada.";
    public const string Flash_Allocated = "Asignación por línea actualizada.";
    public const string Flash_Deleted = "Evidencia eliminada.";

    // Errors (view-level; service refusals come from EvidenceReasons)
    public const string Error_FileRequired = "Debe seleccionar un archivo.";
    public const string Error_FileType = "Tipo de archivo no permitido.";
    public const string Error_InvalidInput = "Revise los datos ingresados.";

    // Closure (US3)
    public const string Closure_Action_Close = "Cerrar línea";
    public const string Closure_Action_Reopen = "Reabrir línea";
    public const string Closure_ReasonLabel = "Motivo";
    public const string Closure_ReopenReasonHint = "Indique el motivo de la reapertura (obligatorio).";
    public const string Closure_ConfirmCloseTitle = "Cerrar línea presupuestaria";
    public const string Closure_ConfirmCloseBody =
        "¿Confirma el cierre de esta línea? La evidencia quedará bloqueada hasta que se reabra.";
    public const string Closure_ConfirmCloseLabel = "Cerrar línea";
    public const string Closure_Flash_Closed = "Línea cerrada.";
    public const string Closure_Flash_Reopened = "Línea reabierta.";
    public const string Closure_Badge_Closed = "Cerrada";

    /// <summary>Spec 047 — es-CR label for an evidence type.</summary>
    public static string TypeLabel(EvidenceType type) => type switch
    {
        EvidenceType.BankReceipt => "Comprobante bancario",
        EvidenceType.Invoice => "Factura",
        EvidenceType.SignedAcceptance => "Acta de aceptación",
        EvidenceType.CreditNote => "Nota de crédito",
        EvidenceType.RefundReceipt => "Comprobante de reintegro",
        EvidenceType.Other => "Otro",
        _ => type.ToString(),
    };

    /// <summary>Tabler badge colour for the evidence-type pill (never the sole signal — the label accompanies it).</summary>
    public static string TypeBadgeClass(EvidenceType type) => type switch
    {
        EvidenceType.BankReceipt => "bg-blue-lt",
        EvidenceType.Invoice => "bg-azure-lt",
        EvidenceType.SignedAcceptance => "bg-green-lt",
        EvidenceType.CreditNote => "bg-orange-lt",
        EvidenceType.RefundReceipt => "bg-red-lt",
        EvidenceType.Other => "bg-secondary-lt",
        _ => "bg-secondary-lt",
    };
}
