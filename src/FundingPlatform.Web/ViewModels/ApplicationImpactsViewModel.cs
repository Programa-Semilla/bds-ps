using Microsoft.AspNetCore.Mvc.Rendering;

namespace FundingPlatform.Web.ViewModels;

/// <summary>
/// Spec 035 (evolved 2026-06-16, US2 / D15) — the application-level "Impactos" manager:
/// the impacts already declared (list + remove) and the "add impact" form (active
/// template picker → dynamic parameter fields via the kept TemplateParameters endpoint).
/// </summary>
public class ApplicationImpactsViewModel
{
    public int ApplicationId { get; set; }
    public string? CompanyName { get; set; }

    /// <summary>The impacts already declared on the application.</summary>
    public List<DeclaredImpactRow> DeclaredImpacts { get; set; } = new();

    /// <summary>Active impact templates offered by the "add impact" picker.</summary>
    public List<SelectListItem> ActiveTemplates { get; set; } = new();

    /// <summary>True when no active impact templates exist (D7 empty-state).</summary>
    public bool HasActiveTemplates => ActiveTemplates.Count > 0;
}

/// <summary>One declared application impact with its captured values, for the list.</summary>
public class DeclaredImpactRow
{
    public int ApplicationImpactId { get; set; }
    public string TemplateName { get; set; } = string.Empty;
    public List<ImpactParameterDisplayViewModel> Parameters { get; set; } = new();
}
