namespace FundingPlatform.Web.Resources;

/// <summary>
/// Localized (es-CR) strings for the reviewer queue (spec 016 — FR-014 search
/// input). NFR-004 requires es-CR for every new admin-area / reviewer-area
/// string.
/// </summary>
public static class ReviewerQueueResources
{
    public const string SearchLabel = "Buscar solicitante";
    // Spec 032 — widened to also match identification and the admin-assigned User Code.
    public const string SearchPlaceholder = "Nombre, cédula o código de usuario";
    public const string SearchSubmit = "Buscar";
    public const string SearchClear = "Limpiar";
}
