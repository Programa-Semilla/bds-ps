using Microsoft.AspNetCore.Mvc.Rendering;

namespace FundingPlatform.Web.ViewModels;

public class ApplicationViewModel
{
    public int Id { get; set; }
    /// <summary>Spec 021 / FR-008 — opaque PublicCode (e.g. <c>A7K2-9XF3</c>).</summary>
    public string? PublicCode { get; set; }
    public string? CompanyName { get; set; }
    public string State { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? SubmittedAt { get; set; }
    public List<ItemViewModel> Items { get; set; } = new();

    /// <summary>
    /// Spec 035 (evolved 2026-06-16, D16) — the application's declared impacts (app level).
    /// </summary>
    public List<ApplicationImpactDisplayViewModel> Impacts { get; set; } = new();

    /// <summary>Spec 035 (evolved 2026-06-16, D16) — submit gate: ≥1 declared impact and
    /// every item attributed + justified. Required category/impact values are gated
    /// server-side at submit.</summary>
    public bool ReadyForSubmit =>
        Items.Count > 0
        && Impacts.Count > 0
        && Items.All(i => i.HasImpactAttribution && !string.IsNullOrWhiteSpace(i.ImpactJustification));

    /// <summary>Spec 021 — active categories (kept for the draft editor toolbar).</summary>
    public List<SelectListItem> Categories { get; set; } = new();

    /// <summary>
    /// Spec 015 / T413 — application-summary computed total in CRC. Sums each
    /// Item's selected-supplier <c>Quotation.ConvertedCrcAmount</c>, excluding
    /// rows flagged <c>LegacyNeedsReview = true</c> (FR-026 — legacy rows are
    /// quarantined out of cross-currency totals until an admin attaches a rate).
    /// Null when no Item has a selected supplier yet (Draft).
    /// </summary>
    public decimal? TotalConvertedCrc { get; set; }

    /// <summary>True when at least one Item's selected-supplier quotation is flagged legacy.</summary>
    public bool HasLegacyNeedsReview { get; set; }
}

public class ItemViewModel
{
    public int Id { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string CategoryName { get; set; } = string.Empty;
    public int QuotationCount { get; set; }

    /// <summary>Spec 035 (evolved 2026-06-16, D14) — names of the application impacts
    /// this line item is attributed to + the short justification.</summary>
    public List<string> AttributedImpactNames { get; set; } = new();
    public string? ImpactJustification { get; set; }
    public bool HasImpactAttribution => AttributedImpactNames.Count > 0;

    /// <summary>Spec 035 / D1 — per-item category field label/value pairs.</summary>
    public List<CategoryFieldDisplayViewModel> CategoryFields { get; set; } = new();

    public string? ReviewComment { get; set; }

    /// <summary>True when the reviewer flagged the item's quotations as not
    /// technically equivalent. Drives a localized message on Details instead of a
    /// persisted English ReviewComment.</summary>
    public bool IsNotTechnicallyEquivalent { get; set; }

    /// <summary>Spec 015 / T413 — populated from the reviewer's selection so the
    /// application-summary total can pick the right per-Item quotation. Null until
    /// the reviewer chooses a supplier on the item.</summary>
    public int? SelectedSupplierId { get; set; }

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

    // Spec 023 — surfaced so the Application/Edit view can render the per-row
    // affordance (Editar) and any subsequent vigencia summary without re-querying.
    public int SupplierBranchId { get; set; }
    public DateOnly ValidUntil { get; set; }

    // Spec 023 / FR-013 (evolution) — surfaced so the Application/Edit and
    // Application/Details views can build the Descargar link without an extra
    // EF round-trip for the Document row.
    public int DocumentId { get; set; }
    public string? DocumentFileName { get; set; }
}
