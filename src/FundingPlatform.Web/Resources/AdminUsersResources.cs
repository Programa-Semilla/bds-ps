namespace FundingPlatform.Web.Resources;

/// <summary>
/// Localized (es-CR) strings for the admin-facing user CRUD (spec 016 group
/// selector additions). NFR-004 requires es-CR for every new admin-area string.
/// </summary>
public static class AdminUsersResources
{
    public const string GroupSelectorLabel = "Grupos";
    public const string GroupSelectorHelpText =
        "Elija el fondo, luego el proceso y marque los grupos. Puede combinar grupos de distintos procesos; los seleccionados se conservan abajo. Los revisores solo verán solicitantes de los grupos compartidos.";
    public const string GroupSelectorEmptyOption = "(sin grupos disponibles)";

    // Drill-down (Fondo → Proceso → Grupo) labels.
    public const string GroupSelectorFundLabel = "Fondo";
    public const string GroupSelectorFundPlaceholder = "Seleccione un fondo";
    public const string GroupSelectorProcessLabel = "Proceso";
    public const string GroupSelectorProcessPlaceholder = "Seleccione un proceso";
    public const string GroupSelectorGroupsLabel = "Grupos del proceso";
    public const string GroupSelectorGroupsPlaceholder = "Elija un proceso para ver sus grupos.";
    public const string GroupSelectorSelectedLabel = "Grupos seleccionados";
    public const string GroupSelectorNoneSelected = "Aún no ha seleccionado ningún grupo.";
    public const string GroupSelectorRemoveLabel = "Quitar";

    // Validation
    public const string AtLeastOneGroupRequired = "Debes seleccionar al menos un grupo.";
    public const string GroupNotFound = "Uno o más grupos seleccionados ya no existen.";
    public const string ConcurrencyConflict =
        "Otro administrador modificó este usuario al mismo tiempo. Vuelve a intentar con los datos actualizados.";

    // Spec 032 — admin-assigned User Code (Solicitante).
    public const string UserCodeLabel = "Código de usuario";
    public const string UserCodeHelpText = "Código asignado por la administración para identificar al solicitante.";
    public const string UserCodeRequired = "El código de usuario es obligatorio para el rol Solicitante.";
    public const string UserCodeInUse = "El código de usuario ya está en uso.";
    public const string UserCodeColumnHeader = "Código de usuario";
    public const string UserCodeEmptyPlaceholder = "—";
    // Spec 032 — widened user-list search placeholder.
    public const string SearchPlaceholder = "Nombre, correo, identificación o código de usuario";
}
