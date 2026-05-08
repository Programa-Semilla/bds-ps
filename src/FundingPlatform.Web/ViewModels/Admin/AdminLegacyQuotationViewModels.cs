namespace FundingPlatform.Web.ViewModels.Admin;

/// <summary>
/// Spec 015 / US6 — single row of the admin "Cotizaciones Pendientes" queue.
/// Each row carries the picker options (the rate-history list filtered to the
/// row's currency pair) so the admin can attach a historical rate inline
/// without round-tripping through a second screen.
/// </summary>
public class AdminLegacyQuotationRowViewModel
{
    public int QuotationId { get; set; }
    public int ApplicationId { get; set; }
    public int ItemId { get; set; }
    public string ItemName { get; set; } = string.Empty;
    public string SupplierName { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string Currency { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }

    /// <summary>Rate-picker options ordered by EffectiveAtUtc descending.</summary>
    public List<AdminLegacyQuotationRateOption> RateOptions { get; set; } = new();
}

public class AdminLegacyQuotationRateOption
{
    public Guid Id { get; set; }
    public decimal BuyRate { get; set; }
    public DateTime EffectiveAtUtc { get; set; }
}

public class AdminLegacyQuotationsListViewModel
{
    public List<AdminLegacyQuotationRowViewModel> Rows { get; set; } = new();
}
