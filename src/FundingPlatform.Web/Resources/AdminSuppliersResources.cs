namespace FundingPlatform.Web.Resources;

/// <summary>
/// Localized (es-CR) strings for the admin-facing supplier catalog management (spec 013).
/// </summary>
public static class AdminSuppliersResources
{
    public const string Page_Title = "Catálogo de proveedores";
    public const string FilterStatus_PendingReview = "Pendientes de revisión";
    public const string FilterStatus_Verified = "Verificados";
    public const string FilterStatus_Rejected = "Rechazados";
    public const string FilterStatus_All = "Todos";
    public const string Verify_Confirm = "¿Verificar este proveedor?";
    public const string Reject_RequireReason = "Indica la razón de rechazo.";

    public const string RejectedSuppliersBannerTemplate =
        "Esta postulación referencia {0} proveedor(es) rechazado(s) por administración.";
}
