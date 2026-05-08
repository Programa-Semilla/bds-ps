namespace FundingPlatform.Web.Resources;

/// <summary>
/// Localized (es-CR) strings for the admin-facing user CRUD (spec 016 group
/// selector additions). NFR-004 requires es-CR for every new admin-area string.
/// </summary>
public static class AdminUsersResources
{
    public const string GroupSelectorLabel = "Grupos";
    public const string GroupSelectorHelpText =
        "Selecciona uno o más grupos. Los revisores solo verán solicitantes de los grupos compartidos.";
    public const string GroupSelectorEmptyOption = "(sin grupos disponibles)";

    // Validation
    public const string AtLeastOneGroupRequired = "Debes seleccionar al menos un grupo.";
    public const string GroupNotFound = "Uno o más grupos seleccionados ya no existen.";
    public const string ConcurrencyConflict =
        "Otro administrador modificó este usuario al mismo tiempo. Vuelve a intentar con los datos actualizados.";
}
