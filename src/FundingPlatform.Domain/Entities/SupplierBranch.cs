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

    /// <summary>Spec 025 / FR-001 — FK into the <see cref="Entities.District"/> catalog
    /// (third cascade tier). Nullable: legacy rows carry NULL, and the domain permits a
    /// province+cantón pair without a distrito. When set, <see cref="CantonId"/> must
    /// also be set and the district's <see cref="District.CantonId"/> must equal it.</summary>
    public int? DistrictId { get; private set; }
    public District? DistrictRef { get; private set; }

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
    /// Spec 021 / FR-014 + Spec 025 / FR-006 — sets the Provincia → Cantón →
    /// Distrito FK chain. Invariant (superset of the spec-021 version):
    /// <list type="number">
    ///   <item><paramref name="provinceId"/> and <paramref name="cantonId"/> are
    ///   both null or both set (unchanged).</item>
    ///   <item>When <paramref name="cantonId"/> is set, <paramref name="canton"/>
    ///   is non-null, <c>canton.Id == cantonId</c>, and
    ///   <c>canton.ProvinceId == provinceId</c> (unchanged).</item>
    ///   <item><b>New:</b> when <paramref name="districtId"/> is set,
    ///   <paramref name="cantonId"/> must be set, <paramref name="district"/> is
    ///   non-null, <c>district.Id == districtId</c>, and
    ///   <c>district.CantonId == cantonId</c>.</item>
    /// </list>
    /// A distrito-less province+cantón pair is permitted at the domain layer (the
    /// orphaned spec-021 inline path still uses it); the all-three-required rule is
    /// enforced at the form/controller layer for the three wired surfaces (plan
    /// Decision 6 — tracked deviation vs FR-006).
    /// </summary>
    /// <exception cref="ArgumentException">
    /// Province/Cantón arity mismatch; cantón's province mismatch; distrito set
    /// without a cantón; or the entities do not match their id arguments.
    /// </exception>
    public void SetLocation(int? provinceId, int? cantonId, int? districtId, Canton? canton, District? district)
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
        if (districtId is not null)
        {
            if (cantonId is null)
            {
                throw new ArgumentException(
                    "DistrictId cannot be set without a CantonId.", nameof(districtId));
            }
            ArgumentNullException.ThrowIfNull(district);
            if (district.Id != districtId.Value)
            {
                throw new ArgumentException(
                    "District entity does not match districtId argument.", nameof(district));
            }
            if (district.CantonId != cantonId.Value)
            {
                throw new ArgumentException(
                    "District.CantonId must equal the branch CantonId (FR-006).",
                    nameof(district));
            }
        }

        ProvinceId = provinceId;
        CantonId = cantonId;
        CantonRef = canton;
        DistrictId = districtId;
        DistrictRef = district;
        UpdatedAt = DateTime.UtcNow;
    }
}
