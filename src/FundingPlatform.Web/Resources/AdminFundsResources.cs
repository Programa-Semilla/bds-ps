namespace FundingPlatform.Web.Resources;

/// <summary>
/// Localized (es-CR) strings for the admin-facing Fund (Fondo) catalog
/// management (spec 029). NFR-004 requires every new admin-area string to
/// participate in es-CR localization.
/// </summary>
public static class AdminFundsResources
{
    // Page titles + nav
    public const string Page_Title = "Catálogo de fondos";
    public const string Page_Subtitle = "Administra los fondos del programa. Cada proceso pertenece a un fondo; archivar un fondo congela sus postulaciones.";
    public const string Breadcrumb_Index = "Fondos";
    public const string Breadcrumb_Create = "Crear fondo";
    public const string Breadcrumb_Edit = "Editar fondo";
    public const string Breadcrumb_Details = "Detalle del fondo";

    // Actions
    public const string Action_Create = "Crear fondo";
    public const string Action_Edit = "Editar";
    public const string Action_Details = "Ver detalle";
    public const string Action_Save = "Guardar";
    public const string Action_Cancel = "Cancelar";
    public const string Action_Archive = "Archivar";
    public const string Action_Reactivate = "Reactivar";
    public const string Action_Back = "Atrás";
    public const string Action_UploadRegulation = "Subir reglamento";
    public const string Action_ReplaceRegulation = "Reemplazar reglamento";
    public const string Action_RemoveRegulation = "Eliminar reglamento";
    public const string Action_DownloadRegulation = "Descargar reglamento";

    // Table headers
    public const string Column_Name = "Nombre";
    public const string Column_Status = "Estado";
    public const string Column_ProcessCount = "Procesos";
    public const string Column_Regulation = "Reglamento";

    // Status labels
    public const string Status_Active = "Activo";
    public const string Status_Archived = "Archivado";

    // Filters
    public const string Filter_All = "Todos";
    public const string Filter_Active = "Activos";
    public const string Filter_Archived = "Archivados";

    // Fields
    public const string Field_Name = "Nombre del fondo";
    public const string Field_Description = "Descripción";
    public const string Field_Regulation = "Reglamento (PDF, opcional)";
    public const string Regulation_None = "Sin reglamento";
    public const string Regulation_Present = "Reglamento disponible";

    // Confirmations (spec 024 data-confirm)
    public const string Confirm_Archive = "¿Archivar este fondo? Sus postulaciones quedarán congeladas y ocultas para postulantes y revisores.";
    public const string Confirm_Reactivate = "¿Reactivar este fondo? Sus postulaciones volverán a estar disponibles.";
    public const string Confirm_RemoveRegulation = "¿Eliminar el reglamento de este fondo?";

    // Validation / errors
    public const string Error_NameRequired = "El nombre del fondo es obligatorio.";
    public const string Error_DuplicateName = "Ya existe un fondo con ese nombre.";
    public const string Error_DescriptionRequired = "La descripción es obligatoria.";
    public const string Error_NotPdf = "Solo se aceptan archivos PDF.";
    public const string Error_FileTooLarge = "El archivo excede el tamaño máximo permitido.";
    public const string Error_FileRequired = "Debe seleccionar un archivo PDF.";

    // Flash (toast) messages
    public const string Flash_Created = "Fondo creado.";
    public const string Flash_Updated = "Fondo actualizado.";
    public const string Flash_Archived = "Fondo archivado.";
    public const string Flash_Reactivated = "Fondo reactivado.";
    public const string Flash_RegulationSet = "Reglamento actualizado.";
    public const string Flash_RegulationRemoved = "Reglamento eliminado.";

    // Empty state
    public const string Empty_Title = "Aún no hay fondos.";
    public const string Empty_Subtitle = "Cree el primer fondo para agrupar los procesos del programa.";
    public const string Details_NoProcesses = "Este fondo todavía no tiene procesos asociados.";
}
