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

    /// <summary>Spec 021 / FR-012 — branch contact (separate from the Supplier contact).</summary>
    public string? ContactPersonName { get; private set; }

    /// <summary>Spec 021 / FR-014 — FK into the <see cref="Entities.Province"/> catalog.
    /// Nullable during migration; once the cascade UI ships, applicant + admin
    /// supplier-branch flows always set both this and <see cref="CantonId"/>.</summary>
    public int? ProvinceId { get; private set; }
    public Province? ProvinceRef { get; private set; }

    /// <summary>Spec 021 / FR-014 — FK into the <see cref="Entities.Canton"/> catalog.
    /// Same nullability invariant as <see cref="ProvinceId"/>.</summary>
    public int? CantonId { get; private set; }
    public Canton? CantonRef { get; private set; }

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

    /// <summary>
    /// Spec 021 / FR-012 — sets the branch contact-person name. Nullable; trimmed on set.
    /// </summary>
    public void SetContactPersonName(string? contactPersonName)
    {
        ContactPersonName = string.IsNullOrWhiteSpace(contactPersonName)
            ? null
            : contactPersonName.Trim();
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Spec 021 / FR-014 — sets the Provincia + Cantón FK pair. Either both null
    /// (catalog data not yet captured for this branch) or both non-null. When
    /// both non-null, <see cref="CantonCatalogEntry.ProvinceId"/> MUST equal the
    /// branch's <see cref="ProvinceId"/>; otherwise the spec Edge Case copy
    /// applies (*"Solo proveedores con dirección en Costa Rica"*).
    /// </summary>
    /// <exception cref="ArgumentException">
    /// ProvinceId/CantonId arity mismatch, or canton's province does not match
    /// the branch's province.
    /// </exception>
    public void SetLocation(int? provinceId, int? cantonId, Canton? canton)
    {
        if ((provinceId is null) != (cantonId is null))
        {
            throw new ArgumentException(
                "ProvinceId and CantonId must both be set or both be null.",
                provinceId is null ? nameof(cantonId) : nameof(provinceId));
        }
        if (cantonId is not null)
        {
            ArgumentNullException.ThrowIfNull(canton);
            if (canton.Id != cantonId.Value)
            {
                throw new ArgumentException(
                    "Canton entity does not match cantonId argument.", nameof(canton));
            }
            if (canton.ProvinceId != provinceId!.Value)
            {
                throw new ArgumentException(
                    "Canton.ProvinceId must equal the branch ProvinceId (FR-014).",
                    nameof(canton));
            }
        }

        ProvinceId = provinceId;
        CantonId = cantonId;
        CantonRef = canton;
        UpdatedAt = DateTime.UtcNow;
    }
}
