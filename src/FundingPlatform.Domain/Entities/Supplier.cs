using System.Text.RegularExpressions;
using FundingPlatform.Domain.Enums;
using FundingPlatform.Domain.ValueObjects;

namespace FundingPlatform.Domain.Entities;

/// <summary>
/// Aggregate root for the centralized supplier catalog (spec 013). Owns its
/// lifecycle (Draft -> PendingReview -> Verified | Rejected) and a 1:N
/// collection of branches. All branch CRUD goes through this aggregate.
/// </summary>
public partial class Supplier
{
    private readonly List<SupplierBranch> _branches = [];

    public int Id { get; private set; }
    public string LegalId { get; private set; } = string.Empty;

    /// <summary>Spec 026 — kind of legal ID (Cédula jurídica / NITE). Nullable for legacy rows.</summary>
    public IdentificationType? IdentificationType { get; private set; }

    public string Name { get; private set; } = string.Empty;

    // Admin-only flags (FR-040). Applicants never see these on a form.
    public bool HasElectronicInvoice { get; private set; }
    public bool IsCompliantCCSS { get; private set; }
    public bool IsCompliantHacienda { get; private set; }
    public bool IsCompliantSICOP { get; private set; }

    // Lifecycle (FR-021, FR-024, FR-035).
    public SupplierVerificationStatus VerificationStatus { get; private set; }
    public int? CreatedByApplicantId { get; private set; }
    public string? VerifiedByUserId { get; private set; }
    public DateTime? VerifiedAt { get; private set; }
    public string? RejectionReason { get; private set; }

    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }

    public IReadOnlyCollection<SupplierBranch> Branches => _branches.AsReadOnly();

    private Supplier() { }

    /// <summary>
    /// Applicant-initiated factory (FR-021). Creates a Draft supplier with one
    /// default branch built from the given fields. The applicant has no input on
    /// compliance flags or the e-invoice flag — those are admin-only at every status.
    /// </summary>
    public static Supplier CreateDraft(
        string legalId,
        string name,
        int createdByApplicantId,
        string firstBranchName,
        string? firstBranchContactName,
        string? firstBranchEmail,
        string? firstBranchPhone,
        string? firstBranchAddressLine,
        string? firstBranchProvince,
        string? firstBranchShippingDetails,
        string? firstBranchWarrantyInfo,
        int? firstBranchProvinceId = null,
        int? firstBranchCantonId = null,
        int? firstBranchDistrictId = null,
        Canton? firstBranchCanton = null,
        District? firstBranchDistrict = null,
        IdentificationType? identificationType = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(legalId);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        // Spec 026 — when a type is supplied, route the legal ID through the VO so the
        // stored value is canonical; the type-agnostic NormalizeLegalId yields the same
        // 1-3-6 shape for the 10-digit jurídica/NITE forms, keeping lookup consistent.
        var canonicalLegalId = identificationType is { } type
            ? Identification.From(type, legalId).Value
            : NormalizeLegalId(legalId);

        var s = new Supplier
        {
            LegalId = canonicalLegalId,
            IdentificationType = identificationType,
            Name = name.Trim(),
            CreatedByApplicantId = createdByApplicantId,
            VerificationStatus = SupplierVerificationStatus.Draft,
            HasElectronicInvoice = false,
            IsCompliantCCSS = false,
            IsCompliantHacienda = false,
            IsCompliantSICOP = false,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };

        var defaultBranch = new SupplierBranch(
            firstBranchName,
            firstBranchContactName,
            firstBranchEmail,
            firstBranchPhone,
            firstBranchAddressLine,
            firstBranchProvince,
            firstBranchShippingDetails,
            firstBranchWarrantyInfo,
            isDefault: true,
            createdByApplicantId: createdByApplicantId);
        // Spec 025 — set the structured location chain on the aggregate when supplied
        // by the cascade write path (province+cantón[+distrito]). All-null otherwise.
        if (firstBranchProvinceId is not null || firstBranchCanton is not null)
        {
            defaultBranch.SetLocation(
                firstBranchProvinceId, firstBranchCantonId, firstBranchDistrictId,
                firstBranchCanton, firstBranchDistrict);
        }
        s._branches.Add(defaultBranch);
        return s;
    }

    /// <summary>
    /// Normalizes a legal ID for canonical comparison (FR-001, FR-005). Strips
    /// non-alphanumerics and uppercases; a bare 10-digit value (cédula jurídica /
    /// NITE) is regrouped to the canonical <c>1-3-6</c> hyphenated form so that a
    /// query typed with, without, or with arbitrary separators all converge on the
    /// stored value (spec 026 FR-013). Type-agnostic — both supplier ID kinds share
    /// the 10-digit shape. Use on every read and write of a legal ID.
    /// </summary>
    public static string NormalizeLegalId(string legalId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(legalId);
        var stripped = NonAlnum().Replace(legalId, string.Empty).ToUpperInvariant();
        if (stripped.Length == 10 && AllDigits().IsMatch(stripped))
        {
            return $"{stripped[0]}-{stripped.Substring(1, 3)}-{stripped.Substring(4, 6)}";
        }
        return stripped;
    }

    [GeneratedRegex(@"[^A-Za-z0-9]", RegexOptions.CultureInvariant)]
    private static partial Regex NonAlnum();

    [GeneratedRegex(@"^\d+$", RegexOptions.CultureInvariant)]
    private static partial Regex AllDigits();

    // ----------- Lifecycle methods (Constitution II: rich domain model) -----------

    /// <summary>
    /// Application-submission side effect (FR-024). Idempotent on non-Draft
    /// statuses — a supplier already past Draft is silently left alone.
    /// </summary>
    public void SubmitForReview()
    {
        if (VerificationStatus != SupplierVerificationStatus.Draft)
            return;
        VerificationStatus = SupplierVerificationStatus.PendingReview;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Admin verifies the supplier (FR-035). Records the verifier identity and
    /// timestamp. Clears any prior RejectionReason. Throws if attempted on a
    /// Draft supplier (admin must wait for SubmitForReview first).
    /// </summary>
    public void Verify(string verifiedByUserId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(verifiedByUserId);
        if (VerificationStatus == SupplierVerificationStatus.Draft)
            throw new InvalidOperationException("Cannot verify a Draft supplier; submit for review first.");
        VerificationStatus = SupplierVerificationStatus.Verified;
        VerifiedByUserId = verifiedByUserId;
        VerifiedAt = DateTime.UtcNow;
        RejectionReason = null;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Admin rejects the supplier with a required reason (FR-035).
    /// </summary>
    public void Reject(string verifiedByUserId, string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(verifiedByUserId);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        if (VerificationStatus == SupplierVerificationStatus.Draft)
            throw new InvalidOperationException("Cannot reject a Draft supplier.");
        VerificationStatus = SupplierVerificationStatus.Rejected;
        VerifiedByUserId = verifiedByUserId;
        VerifiedAt = DateTime.UtcNow;
        RejectionReason = reason.Trim();
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Applicant edits the supplier name while parent application is Draft (FR-022).
    /// Throws on any non-Draft status — admins use EditByAdmin instead.
    /// </summary>
    public void RenameByApplicant(string newName)
    {
        if (VerificationStatus != SupplierVerificationStatus.Draft)
            throw new InvalidOperationException("Applicants cannot rename non-Draft suppliers.");
        ArgumentException.ThrowIfNullOrWhiteSpace(newName);
        Name = newName.Trim();
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Admin edits identity fields including the four admin-only flags
    /// (FR-032, FR-033). Permitted at any status; takes effect immediately.
    /// </summary>
    public void EditByAdmin(
        string newName,
        bool hasElectronicInvoice,
        bool isCompliantCCSS,
        bool isCompliantHacienda,
        bool isCompliantSICOP)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(newName);
        Name = newName.Trim();
        HasElectronicInvoice = hasElectronicInvoice;
        IsCompliantCCSS = isCompliantCCSS;
        IsCompliantHacienda = isCompliantHacienda;
        IsCompliantSICOP = isCompliantSICOP;
        UpdatedAt = DateTime.UtcNow;
    }

    // ----------- Branch operations (single source of truth for invariants) -----------

    /// <summary>
    /// Adds a new branch under the supplier. Enforces the "exactly one default"
    /// invariant in-process; the DB also enforces it via a filtered unique index.
    /// </summary>
    public SupplierBranch AddBranch(
        string branchName,
        string? contactName,
        string? email,
        string? phone,
        string? addressLine,
        string? province,
        string? shippingDetails,
        string? warrantyInfo,
        int? createdByApplicantId,
        bool isDefault = false,
        int? provinceId = null,
        int? cantonId = null,
        int? districtId = null,
        Canton? canton = null,
        District? district = null)
    {
        if (isDefault && _branches.Any(b => b.IsDefault))
            throw new InvalidOperationException("Supplier already has a default branch.");
        var branch = new SupplierBranch(
            branchName, contactName, email, phone, addressLine, province,
            shippingDetails, warrantyInfo, isDefault, createdByApplicantId);
        // Spec 025 — structured location chain when supplied by the cascade write path.
        if (provinceId is not null || canton is not null)
        {
            branch.SetLocation(provinceId, cantonId, districtId, canton, district);
        }
        _branches.Add(branch);
        UpdatedAt = DateTime.UtcNow;
        return branch;
    }

    /// <summary>
    /// Edits a branch's contact fields (FR-014, FR-034). Caller is responsible
    /// for the role/ownership check before invoking.
    /// </summary>
    public void EditBranch(
        int branchId,
        string branchName,
        string? contactName,
        string? email,
        string? phone,
        string? addressLine,
        string? province,
        string? shippingDetails,
        string? warrantyInfo,
        int? provinceId = null,
        int? cantonId = null,
        int? districtId = null,
        Canton? canton = null,
        District? district = null)
    {
        var branch = _branches.FirstOrDefault(b => b.Id == branchId)
            ?? throw new InvalidOperationException($"Branch {branchId} not found on supplier {Id}.");
        branch.Edit(branchName, contactName, email, phone, addressLine, province, shippingDetails, warrantyInfo);
        // Spec 025 — apply the structured location chain when supplied by the cascade
        // write path (admin branch edit). Left untouched when no location is provided.
        if (provinceId is not null || canton is not null)
        {
            branch.SetLocation(provinceId, cantonId, districtId, canton, district);
        }
        UpdatedAt = DateTime.UtcNow;
    }
}
