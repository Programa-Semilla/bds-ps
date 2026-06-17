namespace FundingPlatform.Web.Resources;

/// <summary>
/// Spec 037 / D13 — localized (es-CR) strings for the admin-facing company
/// management surfaces (Create form + Edit management card) and the mapping of
/// company <see cref="UserFacingErrorCode"/> values to their es-CR messages.
/// NFR — every new admin-area string is es-CR.
/// </summary>
public static class AdminCompaniesResources
{
    // Card / form labels.
    public const string CardTitle = "Empresas";
    public const string CardHelp =
        "Gestione las empresas asignadas a este solicitante. Debe quedar al menos una empresa activa.";
    public const string NameLabel = "Nombre de la empresa";
    public const string AddButton = "Agregar empresa";
    public const string RenameButton = "Renombrar";
    public const string ArchiveButton = "Archivar";
    public const string UnarchiveButton = "Reactivar";
    public const string ArchivedBadge = "Archivada";
    public const string ActiveBadge = "Activa";
    public const string NoCompanies = "Este solicitante aún no tiene empresas.";
    public const string CreateFieldLabel = "Empresas del solicitante";
    public const string CreateAddButton = "Agregar otra empresa";
    public const string CreateRemoveButton = "Quitar";

    // Confirmations (spec 024 dialog copy).
    public const string ArchiveConfirm = "¿Archivar esta empresa? No estará disponible para nuevas solicitudes.";
    public const string UnarchiveConfirm = "¿Reactivar esta empresa?";

    // Messages.
    public const string AddedToast = "Empresa agregada.";
    public const string RenamedToast = "Empresa renombrada.";
    public const string ArchivedToast = "Empresa archivada.";
    public const string UnarchivedToast = "Empresa reactivada.";

    // Create-form ModelState validation strings used directly by AdminUsersController.
    // (Service-produced company errors — duplicate / archive-last / unarchive-collision /
    //  invalid — are rendered via IUserFacingErrorTranslator, the single es-CR source of
    //  truth shared with the applicant surfaces; they are NOT duplicated here.)
    public const string AtLeastOneRequired = "Debe indicar al menos una empresa para el solicitante.";
    public const string NameTooLong = "El nombre de la empresa supera los 200 caracteres.";
}
