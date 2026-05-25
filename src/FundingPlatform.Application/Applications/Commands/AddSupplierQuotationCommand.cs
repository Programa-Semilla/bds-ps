using FundingPlatform.Domain.Entities;

namespace FundingPlatform.Application.Applications.Commands;

/// <summary>
/// Single command for the applicant's "add supplier + quotation" flow (spec 013).
/// Mutually exclusive sub-payloads:
///   1. SelectedBranchId.HasValue          -> reuse an existing branch (US1)
///   2. NewBranch != null + SupplierId set -> add a new branch under existing supplier (US2)
///   3. NewSupplier != null                -> create a brand-new Draft supplier (US3)
/// Quotation fields (Price, Currency, ValidUntil, file metadata) are always required.
/// </summary>
public class AddSupplierQuotationCommand
{
    public int ApplicationId { get; set; }
    public int ItemId { get; set; }

    // Supplier identity (set in all paths)
    public string SupplierLegalId { get; set; } = string.Empty;

    // Path 1 — existing branch reuse
    public int? SelectedSupplierId { get; set; }
    public int? SelectedBranchId { get; set; }

    // Path 2 — new branch under existing supplier
    public AddBranchInput? NewBranch { get; set; }

    // Path 3 — brand-new supplier
    public NewSupplierInput? NewSupplier { get; set; }

    // Quotation fields
    public decimal Price { get; set; }
    public string Currency { get; set; } = string.Empty;
    public DateOnly ValidUntil { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string FileContentType { get; set; } = string.Empty;
    public long FileSize { get; set; }
}

public class AddBranchInput
{
    public string BranchName { get; set; } = string.Empty;
    public string? ContactName { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? AddressLine { get; set; }

    /// <summary>
    /// Legacy free-text / composed display value written to
    /// <c>SupplierBranch.Province</c>. Spec 025: for cascade-sourced branches the
    /// write path overwrites this with the composed
    /// <c>"{Distrito}, {Cantón}, {Provincia}"</c> string before it reaches the aggregate.
    /// </summary>
    public string? Province { get; set; }

    // Spec 025 — structured location FK ids posted by the 3-tier cascade.
    public int? ProvinceId { get; set; }
    public int? CantonId { get; set; }
    public int? DistrictId { get; set; }

    // Spec 025 — catalog entities resolved + validated by the controller via
    // ILocationCatalogReader, threaded to the aggregate's SetLocation invariant
    // (avoids a second DB round-trip in the write path).
    public Canton? Canton { get; set; }
    public District? District { get; set; }

    public string? ShippingDetails { get; set; }
    public string? WarrantyInfo { get; set; }
}

public class NewSupplierInput
{
    public string Name { get; set; } = string.Empty;
    public AddBranchInput FirstBranch { get; set; } = new();
}
