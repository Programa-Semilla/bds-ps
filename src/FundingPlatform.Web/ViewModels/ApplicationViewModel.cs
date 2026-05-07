namespace FundingPlatform.Web.ViewModels;

public class ApplicationViewModel
{
    public int Id { get; set; }
    public string State { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? SubmittedAt { get; set; }
    public List<ItemViewModel> Items { get; set; } = new();
}

public class ItemViewModel
{
    public int Id { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string CategoryName { get; set; } = string.Empty;
    public int QuotationCount { get; set; }
    public bool HasImpact { get; set; }
    public string? ReviewComment { get; set; }

    /// <summary>
    /// Spec 015 / T114 — per-Item quotation summaries with multi-currency fields.
    /// MVP rendering shows original-currency + converted CRC; US4 polishes this
    /// surface via the <c>MoneyDisplayViewComponent</c>.
    /// </summary>
    public List<QuotationSummaryViewModel> Quotations { get; set; } = new();
}

public class QuotationSummaryViewModel
{
    public int Id { get; set; }
    public string SupplierName { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string Currency { get; set; } = string.Empty;
    public decimal? ConvertedCrcAmount { get; set; }
    public decimal? SnapshotRateValue { get; set; }
    public string? SnapshotRateType { get; set; }
    public DateTime? SnapshotEffectiveAtUtc { get; set; }
    public bool LegacyNeedsReview { get; set; }
}
