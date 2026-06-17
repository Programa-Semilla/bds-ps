namespace FundingPlatform.Web.Resources;

/// <summary>
/// Spec 036 / D10 — es-CR copy for the funds-usage evidence stage. NFR / FR-011:
/// every label, validation/rejection message, the confirm-dialog text, and the
/// empty state participate in es-CR localization (no English literals in views/JS).
/// The size-cap rejection reuses <c>UploadSizeGuardFilter.RejectionMessage</c>.
/// </summary>
public static class FundsUsageEvidenceResources
{
    // Stage heading + intro
    public const string Stage_Title = "Evidencia de uso de fondos";
    public const string Stage_Subtitle = "Sube, anota y administra los documentos que respaldan el uso de los fondos desembolsados.";
    public const string Stage_CardLink = "Evidencia de uso de fondos";

    // Upload form
    public const string Upload_FileLabel = "Archivo";
    public const string Upload_NoteLabel = "Nota (opcional)";
    public const string Upload_Submit = "Subir evidencia";
    public const string Upload_AcceptHint = "Tipos permitidos: PDF, imágenes (PNG, JPG, WebP, HEIC) y documentos de Word/Excel.";
    public const string Upload_NoteCounterSuffix = "/250 caracteres";

    // List columns / labels
    public const string List_FileName = "Archivo";
    public const string List_Note = "Nota";
    public const string List_UploadedBy = "Subido por";
    public const string List_UploadedAt = "Fecha";
    public const string Action_Download = "Descargar";
    public const string Action_Delete = "Eliminar";
    public const string Action_EditNote = "Editar nota";
    public const string Action_SaveNote = "Guardar nota";
    public const string Note_Empty = "Sin nota";

    // Empty state
    public const string Empty_Message = "Aún no se ha subido evidencia para esta postulación.";

    // Flash / validation
    public const string Flash_Uploaded = "Evidencia subida correctamente.";
    public const string Flash_NoteSaved = "Nota actualizada.";
    public const string Flash_Deleted = "Evidencia eliminada.";
    public const string Error_FileRequired = "Selecciona un archivo para subir.";
    public const string Error_FileType = "El tipo de archivo no está permitido. Sube un PDF, una imagen o un documento de Word/Excel.";
    public const string Error_NoteTooLong = "La nota no puede superar los 250 caracteres.";

    // Confirm dialog (spec 024)
    public const string Confirm_DeleteTitle = "Eliminar evidencia";
    public const string Confirm_DeleteBody = "¿Eliminar este archivo de evidencia? Esta acción no se puede deshacer.";
}
