using FundingPlatform.Application.DTOs;
using FundingPlatform.Domain.Enums;
using FundingPlatform.Web.ViewModels.Review;

namespace FundingPlatform.Web.ViewModels;

public class ReviewApplicationViewModel
{
    public int ApplicationId { get; set; }
    public string ApplicantName { get; set; } = string.Empty;
    public decimal? ApplicantPerformanceScore { get; set; }
    public string State { get; set; } = string.Empty;
    public DateTime? SubmittedAt { get; set; }
    public List<ReviewItemViewModel> Items { get; set; } = [];

    /// <summary>Spec 035 (evolved 2026-06-16, D16) — the application's declared impacts.</summary>
    public List<ApplicationImpactDisplayViewModel> Impacts { get; set; } = [];

    /// <summary>
    /// Spec 027 / US4 — shared per-line decision summary rendered read-only
    /// alongside the interactive capture controls (which are unchanged).
    /// </summary>
    public IReadOnlyList<DecisionSummaryLineDto> DecisionSummary { get; set; } = [];

    /// <summary>Spec 027 / US5 — current applicant code, prefilled into the write control.</summary>
    public string? ApplicantCodigoPersonal { get; set; }
    public bool HasUnresolvedItems { get; set; }
    public List<string>? UnresolvedItemWarnings { get; set; }
    /// <summary>Spec 013 FR-052: count of quotations referencing a Rejected supplier.</summary>
    public int RejectedSupplierCount { get; set; }
    /// <summary>Spec 020 — is the current viewer an admin? Drives the "Anular límites" toggle visibility.</summary>
    public bool IsAdmin { get; set; }
    /// <summary>Spec 020 — poll interval for the JS Generate-All status updates (default 3 s).</summary>
    public int PollIntervalSeconds { get; set; } = 3;
}

public class ReviewItemViewModel
{
    public int ItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string CategoryName { get; set; } = string.Empty;
    public string ReviewStatus { get; set; } = string.Empty;
    public string? ReviewComment { get; set; }
    public int? SelectedSupplierId { get; set; }
    public bool IsNotTechnicallyEquivalent { get; set; }
    /// <summary>Spec 018 / FR-012 — the existing LineCode value (null until the
    /// reviewer assigns one) so re-renders after a validation error preserve the
    /// reviewer's input alongside the new decision controls.</summary>
    public string? LineCode { get; set; }
    public List<ReviewQuotationViewModel> Quotations { get; set; } = [];
    /// <summary>Spec 039 / FR-021 — ≥2 eligible providers share the top total.</summary>
    public bool HasRecommendationTie { get; set; }
    /// <summary>Spec 039 / FR-020 — at least one provider is eligible (not CCSS-blocked).</summary>
    public bool HasAnyEligible { get; set; } = true;
    /// <summary>Spec 035 (evolved 2026-06-16, D14) — attributed impact names + justification.</summary>
    public List<string> AttributedImpactNames { get; set; } = [];
    public string? ImpactJustification { get; set; }
    /// <summary>Spec 035 / D1 — per-item category field label/value pairs.</summary>
    public List<CategoryFieldDisplayViewModel> CategoryFields { get; set; } = [];
    /// <summary>Spec 020 — per-item AI comparison region projection.</summary>
    public ItemComparisonViewModel? Comparison { get; set; }
}

public class ReviewQuotationViewModel
{
    public int QuotationId { get; set; }
    public int SupplierId { get; set; }
    public string SupplierName { get; set; } = string.Empty;
    public string SupplierLegalId { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public DateOnly ValidUntil { get; set; }
    public string DocumentFileName { get; set; } = string.Empty;
    public bool IsRecommended { get; set; }

    // Spec 039 — seven-criterion explainable score + eligibility (replaces /4 Score +
    // four compliance bools + IsPreSelected).
    public bool IsEligible { get; set; }
    public SupplierBlockReason BlockReason { get; set; }
    public int Total { get; set; }
    public int PriceScore { get; set; }
    public int DeliveryLeadTimeScore { get; set; }
    public int WarrantyTimeScore { get; set; }
    public int HaciendaScore { get; set; }
    public int CcssScore { get; set; }
    public int SicopScore { get; set; }
    public int PmeOrPymeScore { get; set; }
    public int DeliveryLeadTimeValue { get; set; }
    public DurationUnit DeliveryLeadTimeUnit { get; set; }
    public int WarrantyValue { get; set; }
    public DurationUnit WarrantyUnit { get; set; }

    /// <summary>Spec 013 FR-051: supplier verification flags surfaced to the reviewer.</summary>
    public bool IsSupplierVerified { get; set; }
    public bool IsSupplierRejected { get; set; }

    // Spec 015 / T415 — multi-currency display fields surfaced via MoneyDisplayViewComponent.
    public string Currency { get; set; } = "CRC";
    public decimal? ConvertedCrcAmount { get; set; }
    public decimal? SnapshotRateValue { get; set; }
    public string? SnapshotRateType { get; set; }
    public DateTime? SnapshotEffectiveAtUtc { get; set; }
    public bool LegacyNeedsReview { get; set; }

    /// <summary>Spec 038 (US3) — provider warning + compliance/freshness shown read-only to reviewers.</summary>
    public Application.DTOs.SupplierComplianceSnapshot? Compliance { get; set; }
}

public class ImpactParameterDisplayViewModel
{
    public string Name { get; set; } = string.Empty;
    public string DisplayLabel { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}

/// <summary>
/// Spec 035 (evolved 2026-06-16, D16) — one declared application impact (template name +
/// its parameter values) for the app-level "Impactos" card on display surfaces.
/// </summary>
public class ApplicationImpactDisplayViewModel
{
    public string TemplateName { get; set; } = string.Empty;
    public List<ImpactParameterDisplayViewModel> Parameters { get; set; } = [];
}

/// <summary>Spec 035 / D1 — a category field label/value pair for display surfaces.</summary>
public class CategoryFieldDisplayViewModel
{
    public string Label { get; set; } = string.Empty;
    public string? Value { get; set; }
}
