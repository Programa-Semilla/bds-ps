namespace FundingPlatform.Application.DTOs;

public record SupplierDto(
    int Id,
    string LegalId,
    string Name,
    string? ContactName,
    string? Email,
    string? Phone,
    string? Location,
    string? ShippingDetails,
    string? WarrantyInfo,
    bool IsCompliantCCSS,
    bool IsCompliantHacienda,
    bool IsCompliantSICOP);
