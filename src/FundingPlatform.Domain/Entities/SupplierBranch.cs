namespace FundingPlatform.Domain.Entities;

/// <summary>
/// An office/location/contact under a Supplier (spec 013). Branches do not have
/// their own verification status; they inherit the parent Supplier's status.
/// Constructor is internal so only Supplier.AddBranch can create branches —
/// invariants live on the parent aggregate (Constitution Principle II).
/// </summary>
public class SupplierBranch
{
    public int Id { get; private set; }
    public int SupplierId { get; private set; }
    public string BranchName { get; private set; } = string.Empty;
    public string? ContactName { get; private set; }
    public string? Email { get; private set; }
    public string? Phone { get; private set; }
    public string? AddressLine { get; private set; }
    public string? Province { get; private set; }
    public string? ShippingDetails { get; private set; }
    public string? WarrantyInfo { get; private set; }
    public bool IsDefault { get; private set; }
    public int? CreatedByApplicantId { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }

    private SupplierBranch() { }

    internal SupplierBranch(
        string branchName,
        string? contactName,
        string? email,
        string? phone,
        string? addressLine,
        string? province,
        string? shippingDetails,
        string? warrantyInfo,
        bool isDefault,
        int? createdByApplicantId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(branchName);
        BranchName = branchName.Trim();
        ContactName = contactName?.Trim();
        Email = email?.Trim();
        Phone = phone?.Trim();
        AddressLine = addressLine?.Trim();
        Province = province?.Trim();
        ShippingDetails = shippingDetails?.Trim();
        WarrantyInfo = warrantyInfo?.Trim();
        IsDefault = isDefault;
        CreatedByApplicantId = createdByApplicantId;
        CreatedAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }

    internal void Edit(
        string branchName,
        string? contactName,
        string? email,
        string? phone,
        string? addressLine,
        string? province,
        string? shippingDetails,
        string? warrantyInfo)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(branchName);
        BranchName = branchName.Trim();
        ContactName = contactName?.Trim();
        Email = email?.Trim();
        Phone = phone?.Trim();
        AddressLine = addressLine?.Trim();
        Province = province?.Trim();
        ShippingDetails = shippingDetails?.Trim();
        WarrantyInfo = warrantyInfo?.Trim();
        UpdatedAt = DateTime.UtcNow;
    }
}
