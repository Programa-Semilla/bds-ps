using System.ComponentModel.DataAnnotations;

namespace FundingPlatform.Web.ViewModels;

public class EditDraftSupplierViewModel
{
    public int ApplicationId { get; set; }
    public int ItemId { get; set; }
    public int SupplierId { get; set; }

    [Required(ErrorMessage = "El nombre del proveedor es obligatorio.")]
    [Display(Name = "Razón social del proveedor")]
    [MaxLength(300)]
    public string Name { get; set; } = string.Empty;
}

public class EditBranchByApplicantViewModel
{
    public int ApplicationId { get; set; }
    public int ItemId { get; set; }
    public int SupplierId { get; set; }
    public int BranchId { get; set; }

    [Required(ErrorMessage = "El nombre de la sucursal es obligatorio.")]
    [Display(Name = "Nombre de la sucursal")]
    [MaxLength(200)]
    public string BranchName { get; set; } = string.Empty;

    [MaxLength(200)] public string? ContactName { get; set; }
    [EmailAddress, MaxLength(256)] public string? Email { get; set; }
    [Phone, MaxLength(20)] public string? Phone { get; set; }
    [MaxLength(500)] public string? AddressLine { get; set; }
    [MaxLength(100)] public string? Province { get; set; }
    [MaxLength(500)] public string? ShippingDetails { get; set; }
    [MaxLength(500)] public string? WarrantyInfo { get; set; }
}
