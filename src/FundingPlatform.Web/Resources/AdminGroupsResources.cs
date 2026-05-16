namespace FundingPlatform.Web.Resources;

/// <summary>
/// Localized (es-CR) strings for the admin-facing group catalog management
/// (spec 016 — group-scoped reviewer access). NFR-004 requires every new
/// admin-area string to participate in es-CR localization.
/// </summary>
public static class AdminGroupsResources
{
    // Page titles + nav
    public const string Page_Title = "Catálogo de grupos";
    public const string Page_Subtitle = "Administra los grupos que delimitan la visibilidad de los revisores. Los grupos se crean dentro de un proceso.";
    public const string Breadcrumb_Index = "Grupos";
    public const string Breadcrumb_Edit = "Editar grupo";

    // Actions
    public const string Action_GoToProcesses = "Ir a procesos";
    public const string Action_Edit = "Editar";
    public const string Action_Delete = "Eliminar";
    public const string Action_Save = "Guardar";
    public const string Action_Cancel = "Cancelar";

    // Table headers
    public const string Column_Name = "Nombre";
    public const string Column_Process = "Proceso";
    public const string Column_MemberCount = "Miembros";
    public const string Column_Actions = "Acciones";

    // Form labels
    public const string Label_Name = "Nombre del grupo";
    public const string Label_Process = "Proceso";
    public const string Hint_Process = "Cambiar el proceso reasigna el grupo. Las membresías de revisores se conservan.";

    // Empty state
    public const string EmptyState_Title = "Aún no hay grupos.";
    public const string EmptyState_Body = "Los grupos se crean dentro de un proceso. Abre un proceso para crear su primer grupo.";

    // Delete confirmation
    public const string Delete_ConfirmTitle = "¿Eliminar el grupo \"{0}\"?";
    public const string Delete_ConfirmBody =
        "Se eliminarán {0} miembro(s) de este grupo. Los usuarios afectados conservarán su cuenta y los demás grupos a los que pertenezcan.";
    public const string Delete_Submit = "Eliminar grupo";

    // Validation messages (FR-001)
    // DataAnnotations reflects on ErrorMessageResourceName looking for a public
    // static *property*, not a const/field. Expressed as static getters so the
    // AdminGroupCreate/EditViewModel attribute lookups succeed at request time.
    public static string NameRequired => "El nombre del grupo es obligatorio.";
    public static string NameTooLong => "El nombre del grupo debe tener máximo 100 caracteres.";
    public static string NameAlreadyInUse => "Ya existe un grupo con ese nombre.";

    // Flash messages
    public const string FlashCreated = "Grupo creado.";
    public const string FlashRenamed = "Grupo actualizado.";
    public const string FlashDeleted = "Grupo eliminado.";
}
