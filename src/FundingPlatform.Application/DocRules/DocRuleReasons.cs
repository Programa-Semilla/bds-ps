namespace FundingPlatform.Application.DocRules;

/// <summary>
/// Spec 047 — es-CR refusal/validation strings for the admin required-document rule matrix,
/// produced by the Infrastructure <c>DocumentRuleService</c>. Kept in the Application layer (not
/// <c>Web.Resources</c>) for the same cross-layer reason as <c>EvidenceReasons</c> /
/// <c>DisbursementReasons</c>. Each is paired with a stable <see cref="Codes"/> value.
/// </summary>
public static class DocRuleReasons
{
    public static class Codes
    {
        public const string CategoryNotFound = "CATEGORY_NOT_FOUND";
        public const string DuplicateCategory = "DUPLICATE_CATEGORY";
        public const string Concurrency = "CONCURRENCY";
    }

    public const string CategoryNotFound = "No se encontró la categoría.";
    public const string DuplicateCategory = "Ya existe una regla de documentos para esta categoría.";
    public const string Concurrency =
        "La regla fue modificada por otra persona. Vuelva a cargar la página e intente de nuevo.";
}
