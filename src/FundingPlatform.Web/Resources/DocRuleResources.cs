using FundingPlatform.Domain.Enums;

namespace FundingPlatform.Web.Resources;

/// <summary>
/// Spec 047 — es-CR view/controller copy for the admin required-document rule matrix
/// (<c>/Admin/DocumentRules</c>) and the per-line completeness surface. Service-produced
/// refusals live in <c>DocRuleReasons</c> (Application); this holds the labels/buttons/titles.
/// All es-CR — no English literals in views (Constitution / conventions).
/// </summary>
public static class DocRuleResources
{
    // Admin matrix list
    public const string Title = "Documentos requeridos";
    public const string Subtitle =
        "Configure, por categoría, cuáles documentos de evidencia son obligatorios para cerrar una línea. La regla global aplica cuando una categoría no tiene su propia configuración.";
    public const string Col_Category = "Categoría";
    public const string Col_Required = "Documentos requeridos";
    public const string GlobalDefaultName = "Regla global (predeterminada)";
    public const string Empty = "Aún no hay reglas configuradas.";
    public const string Action_Create = "Nueva regla";
    public const string Action_Edit = "Editar";

    // Create / edit form
    public const string CreateTitle = "Nueva regla de documentos";
    public const string EditTitle = "Editar regla de documentos";
    public const string Field_Category = "Categoría";
    public const string Field_CategoryHint =
        "Deje vacío para editar la regla global (predeterminada).";
    public const string Field_UseGlobalDefault = "Regla global (predeterminada)";
    public const string Matrix_Heading = "Tipos de documento";
    public const string Matrix_Required = "Requerido";
    public const string Action_Save = "Guardar";
    public const string Action_Cancel = "Cancelar";

    // Completeness surface (evidence + disbursement line rows)
    public const string Completeness_Heading = "Documentos requeridos";
    public const string Completeness_Present = "Presente";
    public const string Completeness_Missing = "Falta";
    public const string Completeness_Complete = "Completa";
    public const string Completeness_IncompleteBadge = "Faltan documentos";
    public const string Completeness_None = "Sin documentos requeridos.";

    // Flashes
    public const string Flash_Saved = "Regla de documentos guardada.";

    /// <summary>Spec 047 — es-CR label for an evidence type (delegates to <see cref="EvidenceResources.TypeLabel"/>).</summary>
    public static string TypeLabel(EvidenceType type) => EvidenceResources.TypeLabel(type);
}
