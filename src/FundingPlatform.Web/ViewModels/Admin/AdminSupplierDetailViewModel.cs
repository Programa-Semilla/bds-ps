using FundingPlatform.Domain.Enums;

namespace FundingPlatform.Web.ViewModels.Admin;

public class AdminSupplierDetailViewModel
{
    public int Id { get; init; }
    public string LegalId { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public SupplierVerificationStatus Status { get; init; }

    // Spec 038 — enumerated regulatory statuses + per-field freshness metadata.
    public HaciendaStatus? HaciendaStatus { get; init; }
    public DateTime? HaciendaReviewedAt { get; init; }
    public string? HaciendaReviewedByName { get; init; }
    public RegulatoryReviewSource? HaciendaReviewedSource { get; init; }

    public CcssStatus? CcssStatus { get; init; }
    public DateTime? CcssReviewedAt { get; init; }
    public string? CcssReviewedByName { get; init; }
    public RegulatoryReviewSource? CcssReviewedSource { get; init; }

    public SicopStatus? SicopStatus { get; init; }
    public DateTime? SicopReviewedAt { get; init; }
    public string? SicopReviewedByName { get; init; }
    public RegulatoryReviewSource? SicopReviewedSource { get; init; }

    public bool IsPmeOrPyme { get; init; }
    public bool HasWarning { get; init; }
    public string? WarningNote { get; init; }
    public byte[] RowVersion { get; init; } = [];

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
    bool IsDefault,
    // Spec 025 — per-branch Provincia → Cantón → Distrito cascade (pre-selected to
    // current values). ElementIdPrefix keeps ids unique across the one-form-per-branch
    // edit table.
    LocationCascadeViewModel Location);

public class AdminEditSupplierViewModel
{
    public int SupplierId { get; set; }

    [System.ComponentModel.DataAnnotations.Required]
    [System.ComponentModel.DataAnnotations.MaxLength(300)]
    public string Name { get; set; } = string.Empty;

    // Spec 038 — nullable enumerated statuses (blank = "sin revisar"), PME/PYME,
    // warning + note, and the optimistic-concurrency token.
    public HaciendaStatus? Hacienda { get; set; }
    public CcssStatus? Ccss { get; set; }
    public SicopStatus? Sicop { get; set; }
    public bool IsPmeOrPyme { get; set; }
    public bool HasWarning { get; set; }

    [System.ComponentModel.DataAnnotations.MaxLength(1000)]
    public string? WarningNote { get; set; }

    public byte[] RowVersion { get; set; } = [];
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

    // Spec 025 — Provincia → Cantón → Distrito cascade FK ids (replace the free-text
    // Province input). All three required server-side; composed display string is set
    // by the controller.
    public int? ProvinceId { get; set; }
    public int? CantonId { get; set; }
    public int? DistrictId { get; set; }

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
