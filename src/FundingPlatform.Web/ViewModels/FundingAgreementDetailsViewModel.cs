using FundingPlatform.Application.DTOs;

namespace FundingPlatform.Web.ViewModels;

public class FundingAgreementDetailsViewModel
{
    public SigningStagePanelViewModel Panel { get; set; } = new();
    public FundingAgreementDocumentViewModel? Preview { get; set; }
    public bool HasApplicantResponse { get; set; }

    /// <summary>
    /// Spec 027 / US4 — the shared per-line decision summary rendered on the
    /// funding-agreement page (generate / signing / signed-review states),
    /// replacing the approved-only preview. Screen-only; the PDF document body
    /// is unchanged (FR-009).
    /// </summary>
    public IReadOnlyList<DecisionSummaryLineDto> DecisionSummary { get; set; } = [];

    /// <summary>Spec 027 / US3 — richer applicant detail block (screen-only).</summary>
    public FundingAgreementApplicantBlockViewModel? ApplicantBlock { get; set; }

    /// <summary>
    /// Spec 015 / US5 / T512 / FR-027 — when the controller catches
    /// <c>MissingConversionMetadataException</c> during Generate, the view is
    /// re-rendered directly (no TempData / redirect) so a hard browser reload
    /// still shows the inline Spanish error until an admin attaches a
    /// historical rate to the offending quotation.
    /// </summary>
    public string? MissingConversionInlineError { get; set; }
}
