namespace FundingPlatform.Web.ViewModels;

public class ApplicationListViewModel
{
    public List<ApplicationListItemViewModel> Applications { get; set; } = new();
    /// <summary>Spec 021 / FR-030 — applicant first name for the "Hola, {Nombre}" greeting.</summary>
    public string? GreetingName { get; set; }
}

public class ApplicationListItemViewModel
{
    public int Id { get; set; }
    /// <summary>Spec 021 / FR-008 — opaque identifier rendered in place of the numeric Id.</summary>
    public string? PublicCode { get; set; }
    public string? CompanyName { get; set; }
    public string State { get; set; } = string.Empty;
    public int ItemCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? SubmittedAt { get; set; }
}
