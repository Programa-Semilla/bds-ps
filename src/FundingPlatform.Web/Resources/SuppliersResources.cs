namespace FundingPlatform.Web.Resources;

/// <summary>
/// Localized (es-CR) strings for the applicant-facing supplier catalog flows (spec 013).
/// Kept as a static class to match the existing codebase convention (spec 012):
/// the platform pins to a single locale (es-CR); UserFacingErrorTranslator and other
/// localization sites use inline Spanish strings rather than IStringLocalizer/.resx.
/// </summary>
public static class SuppliersResources
{
    public const string LookupRejectedMessage =
        "El proveedor está rechazado por un administrador. Ponte en contacto con el equipo administrativo si necesitas ayuda.";

    public const string LookupConcurrentBanner =
        "Este proveedor acaba de ser registrado por otro postulante. Selecciona una sucursal o agrega una nueva.";

    public const string BranchPicker_Title = "Selecciona la sucursal del proveedor";
    public const string BranchPicker_AddNew = "Agregar nueva sucursal";
    public const string Branch_Default = "Sede principal";
    public const string PendingVerificationBadge = "Pendiente de verificación";
    public const string NewSupplierForm_Hint =
        "El administrador validará la información del proveedor luego de la postulación.";
}
