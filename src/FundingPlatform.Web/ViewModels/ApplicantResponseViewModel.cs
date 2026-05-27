using FundingPlatform.Application.DTOs;
using FundingPlatform.Domain.Enums;

namespace FundingPlatform.Web.ViewModels;

public class ApplicantResponseViewModel
{
    public int ApplicationId { get; set; }
    public bool IsSubmitted { get; set; }
    public ApplicationState State { get; set; }
    public DateTime? SubmittedAt { get; set; }
    public List<ItemResponseViewModel> Items { get; set; } = [];
    public bool CanOpenAppeal { get; set; }
    public bool HasOpenAppeal { get; set; }
    public string? AppealBlockedReason { get; set; }
    public bool HasFundingAgreement { get; set; }

    /// <summary>
    /// Spec 027 / US4 — shared per-line decision summary; on this surface it adds
    /// the technical specifications the legacy item table lacked.
    /// </summary>
    public IReadOnlyList<DecisionSummaryLineDto> DecisionSummary { get; set; } = [];
}
