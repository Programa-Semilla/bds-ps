namespace FundingPlatform.Web.ViewModels;

/// <summary>
/// Spec 021 / FR-005 — the Impact step. Impact is a per-Application concern
/// captured upfront; this view model is no longer Item-shaped.
/// </summary>
public class ImpactViewModel
{
    public int ApplicationId { get; set; }
    public string? PublicCode { get; set; }
    public string CompanyName { get; set; } = string.Empty;

    /// <summary>
    /// Where to land after saving Impact: <c>"edit"</c> returns to the draft
    /// editor (the US2 impact-first flow); anything else returns to Details
    /// (the surface the legacy item-row "Impacto" link is reached from).
    /// </summary>
    public string? ReturnTo { get; set; }

    public int? SelectedTemplateId { get; set; }
    public List<ImpactTemplateOptionViewModel> Templates { get; set; } = new();
    public List<ImpactParameterInputViewModel> Parameters { get; set; } = new();
}

public class ImpactTemplateOptionViewModel
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
}

public class ImpactParameterInputViewModel
{
    public int ParameterId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string DisplayLabel { get; set; } = string.Empty;
    public string DataType { get; set; } = string.Empty;  // Text, Decimal, Integer, Date
    public bool IsRequired { get; set; }
    public string? Value { get; set; }
}
