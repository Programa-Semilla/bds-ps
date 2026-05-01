using FundingPlatform.Domain.Enums;

namespace FundingPlatform.Web.ViewModels.Admin;

public class AdminSupplierDetailViewModel
{
    public int Id { get; init; }
    public string LegalId { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public SupplierVerificationStatus Status { get; init; }
    public bool HasElectronicInvoice { get; init; }
    public bool IsCompliantCCSS { get; init; }
    public bool IsCompliantHacienda { get; init; }
    public bool IsCompliantSICOP { get; init; }
    public string? VerifiedByUserId { get; init; }
    public DateTime? VerifiedAt { get; init; }
    public string? RejectionReason { get; init; }
    public int? CreatedByApplicantId { get; init; }
    public int ReferencingApplicationCount { get; init; }
    public IReadOnlyList<AdminSupplierBranchRowViewModel> Branches { get; init; }
        = Array.Empty<AdminSupplierBranchRowViewModel>();
}

public record AdminSupplierBranchRowViewModel(
    int Id,
    string BranchName,
    string? ContactName,
    string? Email,
    string? Phone,
    string? AddressLine,
    string? Province,
    string? ShippingDetails,
    string? WarrantyInfo,
    bool IsDefault);

public class AdminEditSupplierViewModel
{
    public int SupplierId { get; set; }

    [System.ComponentModel.DataAnnotations.Required]
    [System.ComponentModel.DataAnnotations.MaxLength(300)]
    public string Name { get; set; } = string.Empty;

    public bool HasElectronicInvoice { get; set; }
    public bool IsCompliantCCSS { get; set; }
    public bool IsCompliantHacienda { get; set; }
    public bool IsCompliantSICOP { get; set; }
}

public class AdminEditBranchViewModel
{
    public int SupplierId { get; set; }
    public int BranchId { get; set; }

    [System.ComponentModel.DataAnnotations.Required]
    [System.ComponentModel.DataAnnotations.MaxLength(200)]
    public string BranchName { get; set; } = string.Empty;

    [System.ComponentModel.DataAnnotations.MaxLength(200)] public string? ContactName { get; set; }
    [System.ComponentModel.DataAnnotations.EmailAddress, System.ComponentModel.DataAnnotations.MaxLength(256)] public string? Email { get; set; }
    [System.ComponentModel.DataAnnotations.Phone, System.ComponentModel.DataAnnotations.MaxLength(20)] public string? Phone { get; set; }
    [System.ComponentModel.DataAnnotations.MaxLength(500)] public string? AddressLine { get; set; }
    [System.ComponentModel.DataAnnotations.MaxLength(100)] public string? Province { get; set; }
    [System.ComponentModel.DataAnnotations.MaxLength(500)] public string? ShippingDetails { get; set; }
    [System.ComponentModel.DataAnnotations.MaxLength(500)] public string? WarrantyInfo { get; set; }
}

public class AdminRejectSupplierViewModel
{
    public int SupplierId { get; set; }

    [System.ComponentModel.DataAnnotations.Required(ErrorMessage = "Indica la razón de rechazo.")]
    [System.ComponentModel.DataAnnotations.MaxLength(1000)]
    public string Reason { get; set; } = string.Empty;
}
