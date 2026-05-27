using FundingPlatform.Domain.Enums;

namespace FundingPlatform.Web.ViewModels;

/// <summary>
/// Spec 027 / US3 — richer applicant detail shown on the funding-agreement page
/// (screen-only; the PDF document body is unchanged per FR-009). Empty optional
/// fields render the neutral "—" placeholder.
/// </summary>
public class FundingAgreementApplicantBlockViewModel
{
    public string CompanyName { get; init; } = "—";
    public string RepresentativeName { get; init; } = "—";
    public string? LegalId { get; init; }
    public IdentificationType? IdentificationType { get; init; }
    public string? Email { get; init; }
    public string? Phone { get; init; }
    public string? CodigoPersonal { get; init; }
    public string Group { get; init; } = "—";
    public DateTime? SubmittedAt { get; init; }
}
