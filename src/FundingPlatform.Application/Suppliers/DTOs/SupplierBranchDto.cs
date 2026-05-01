namespace FundingPlatform.Application.Suppliers.DTOs;

public record SupplierBranchDto(
    int Id,
    int SupplierId,
    string BranchName,
    string? ContactName,
    string? Email,
    string? Phone,
    string? AddressLine,
    string? Province,
    string? ShippingDetails,
    string? WarrantyInfo,
    bool IsDefault,
    int? CreatedByApplicantId);
