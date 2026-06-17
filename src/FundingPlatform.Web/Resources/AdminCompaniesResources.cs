using FundingPlatform.Application.Errors;

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

    // Validation / error messages (canonical strings, contracts/interfaces.md).
    public const string AtLeastOneRequired = "Debe indicar al menos una empresa para el solicitante.";
    public const string NameRequired = "Debe ingresar el nombre de la empresa.";
    public const string NameTooLong = "El nombre de la empresa supera los 200 caracteres.";
    public const string Duplicate = "Ya existe una empresa activa con ese nombre para este solicitante.";
    public const string ArchiveLastActive = "No puede archivar la única empresa activa del solicitante.";
    public const string UnarchiveCollision = "No se puede reactivar: ya existe una empresa activa con ese nombre.";
    public const string NotFound = "Empresa no encontrada.";

    /// <summary>Maps a company <see cref="UserFacingErrorCode"/> to its es-CR message.</summary>
    public static string ForError(UserFacingErrorCode code) => code switch
    {
        UserFacingErrorCode.CompanyNameRequired => NameRequired,
        UserFacingErrorCode.CompanyNameTooLong => NameTooLong,
        UserFacingErrorCode.CompanyNameDuplicate => Duplicate,
        UserFacingErrorCode.CompanyArchiveLastActive => ArchiveLastActive,
        UserFacingErrorCode.CompanyUnarchiveNameCollision => UnarchiveCollision,
        UserFacingErrorCode.CompanyAtLeastOneRequired => AtLeastOneRequired,
        UserFacingErrorCode.CompanyInvalid => NotFound,
        _ => "No se pudo completar la operación sobre la empresa.",
    };
}
