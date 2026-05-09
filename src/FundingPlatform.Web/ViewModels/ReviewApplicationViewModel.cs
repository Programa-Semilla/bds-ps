namespace FundingPlatform.Web.ViewModels;

public class ReviewApplicationViewModel
{
    public int ApplicationId { get; set; }
    public string ApplicantName { get; set; } = string.Empty;
    public decimal? ApplicantPerformanceScore { get; set; }
    public string State { get; set; } = string.Empty;
    public DateTime? SubmittedAt { get; set; }
    public List<ReviewItemViewModel> Items { get; set; } = [];
    public bool HasUnresolvedItems { get; set; }
    public List<string>? UnresolvedItemWarnings { get; set; }
    /// <summary>Spec 013 FR-052: count of quotations referencing a Rejected supplier.</summary>
    public int RejectedSupplierCount { get; set; }
}

public class ReviewItemViewModel
{
    public int ItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string CategoryName { get; set; } = string.Empty;
    public string TechnicalSpecifications { get; set; } = string.Empty;
    public string ReviewStatus { get; set; } = string.Empty;
    public string? ReviewComment { get; set; }
    public int? SelectedSupplierId { get; set; }
    public bool IsNotTechnicallyEquivalent { get; set; }
    /// <summary>Spec 018 / FR-012 — the existing LineCode value (null until the
    /// reviewer assigns one) so re-renders after a validation error preserve the
    /// reviewer's input alongside the new decision controls.</summary>
    public string? LineCode { get; set; }
    public List<ReviewQuotationViewModel> Quotations { get; set; } = [];
    public string? ImpactTemplateName { get; set; }
    public List<ImpactParameterDisplayViewModel> ImpactParameters { get; set; } = [];
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
    public int Score { get; set; }
    public bool ScoreCCSS { get; set; }
    public bool ScoreHacienda { get; set; }
    public bool ScoreSICOP { get; set; }
    public bool ScoreElectronicInvoice { get; set; }
    public bool ScoreLowestPrice { get; set; }
    public bool IsPreSelected { get; set; }
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
}

public class ImpactParameterDisplayViewModel
{
    public string Name { get; set; } = string.Empty;
    public string DisplayLabel { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}
