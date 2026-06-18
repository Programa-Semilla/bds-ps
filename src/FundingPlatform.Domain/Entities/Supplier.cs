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

    // ----- Spec 038 — provider regulatory compliance (auditor-maintained) -----
    // Three enumerated statuses replace the old IsCompliant*/HasElectronicInvoice
    // booleans. null = "sin revisar". Each status carries per-field last-reviewed
    // metadata that drives the freshness display (read directly, not from the audit
    // trail).
    public HaciendaStatus? HaciendaStatus { get; private set; }
    public DateTime? HaciendaLastReviewedAt { get; private set; }
    public string? HaciendaLastReviewedBy { get; private set; }
    public RegulatoryReviewSource? HaciendaLastReviewedSource { get; private set; }

    public CcssStatus? CcssStatus { get; private set; }
    public DateTime? CcssLastReviewedAt { get; private set; }
    public string? CcssLastReviewedBy { get; private set; }
    public RegulatoryReviewSource? CcssLastReviewedSource { get; private set; }

    public SicopStatus? SicopStatus { get; private set; }
    public DateTime? SicopLastReviewedAt { get; private set; }
    public string? SicopLastReviewedBy { get; private set; }
    public RegulatoryReviewSource? SicopLastReviewedSource { get; private set; }

    /// <summary>Spec 038 — provider is a PME/PYME (small/medium enterprise).</summary>
    public bool IsPmeOrPyme { get; private set; }

    /// <summary>Spec 038 — non-blocking warning surfaced to reviewers during review.</summary>
    public bool HasWarning { get; private set; }
    public string? WarningNote { get; private set; }

    /// <summary>Spec 038 / D15 — optimistic-concurrency token (multi-auditor + slice-D API contention).</summary>
    public byte[] RowVersion { get; private set; } = [];

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
    /// Admin/auditor edits the provider name (FR-032). Permitted at any status.
    /// Spec 038 narrowed this to name-only; regulatory compliance, PME/PYME, and
    /// the warning now flow through <see cref="ApplyRegulatoryEdit"/>.
    /// </summary>
    public void EditByAdmin(string newName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(newName);
        Name = newName.Trim();
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>Spec 038 — verbatim domain guard for the warning note length (mirrors RejectionReason).</summary>
    public const int WarningNoteMaxLength = 1000;

    /// <summary>
    /// Spec 038 (US1/US2/US3) — auditor edit of the provider's regulatory
    /// compliance, PME/PYME flag, and warning. For each of the three status fields
    /// whose value changes, stamps that field's last-reviewed metadata
    /// (now / actor / Manual) and emits a <see cref="RegulatoryChangeKind.Changed"/>
    /// record. PME and warning changes emit their own records (no last-reviewed
    /// metadata). Warning is normalized — flag off clears the note; the note is
    /// trimmed and capped at <see cref="WarningNoteMaxLength"/>. Returns one
    /// <see cref="RegulatoryChange"/> per change; an empty list means nothing
    /// changed and the caller writes no audit and no row update.
    /// </summary>
    public IReadOnlyList<RegulatoryChange> ApplyRegulatoryEdit(
        HaciendaStatus? hacienda,
        CcssStatus? ccss,
        SicopStatus? sicop,
        bool isPmeOrPyme,
        bool hasWarning,
        string? warningNote,
        string actorUserId,
        DateTime nowUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actorUserId);

        var changes = new List<RegulatoryChange>();

        if (hacienda != HaciendaStatus)
        {
            changes.Add(new RegulatoryChange(
                RegulatoryChangeField.Hacienda,
                ((byte?)HaciendaStatus)?.ToString(),
                ((byte?)hacienda)?.ToString(),
                RegulatoryChangeKind.Changed,
                RegulatoryReviewSource.Manual));
            HaciendaStatus = hacienda;
            HaciendaLastReviewedAt = nowUtc;
            HaciendaLastReviewedBy = actorUserId;
            HaciendaLastReviewedSource = RegulatoryReviewSource.Manual;
        }

        if (ccss != CcssStatus)
        {
            changes.Add(new RegulatoryChange(
                RegulatoryChangeField.Ccss,
                ((byte?)CcssStatus)?.ToString(),
                ((byte?)ccss)?.ToString(),
                RegulatoryChangeKind.Changed,
                RegulatoryReviewSource.Manual));
            CcssStatus = ccss;
            CcssLastReviewedAt = nowUtc;
            CcssLastReviewedBy = actorUserId;
            CcssLastReviewedSource = RegulatoryReviewSource.Manual;
        }

        if (sicop != SicopStatus)
        {
            changes.Add(new RegulatoryChange(
                RegulatoryChangeField.Sicop,
                ((byte?)SicopStatus)?.ToString(),
                ((byte?)sicop)?.ToString(),
                RegulatoryChangeKind.Changed,
                RegulatoryReviewSource.Manual));
            SicopStatus = sicop;
            SicopLastReviewedAt = nowUtc;
            SicopLastReviewedBy = actorUserId;
            SicopLastReviewedSource = RegulatoryReviewSource.Manual;
        }

        if (isPmeOrPyme != IsPmeOrPyme)
        {
            changes.Add(new RegulatoryChange(
                RegulatoryChangeField.Pme,
                IsPmeOrPyme.ToString(),
                isPmeOrPyme.ToString(),
                RegulatoryChangeKind.Changed,
                RegulatoryReviewSource.Manual));
            IsPmeOrPyme = isPmeOrPyme;
        }

        // Warning normalize: flag off clears the note; whitespace-only → null.
        var normalizedNote = hasWarning ? warningNote?.Trim() : null;
        if (string.IsNullOrEmpty(normalizedNote))
            normalizedNote = null;
        if (normalizedNote is { Length: > WarningNoteMaxLength })
            throw new ArgumentException("La nota de advertencia no puede superar los 1000 caracteres.", nameof(warningNote));

        if (hasWarning != HasWarning || normalizedNote != WarningNote)
        {
            // Encode flag + note so a note-only edit (flag unchanged) still records a
            // meaningful old→new delta in the audit payload.
            changes.Add(new RegulatoryChange(
                RegulatoryChangeField.Warning,
                $"{HasWarning}|{WarningNote}",
                $"{hasWarning}|{normalizedNote}",
                RegulatoryChangeKind.Changed,
                RegulatoryReviewSource.Manual));
            HasWarning = hasWarning;
            WarningNote = normalizedNote;
        }

        if (changes.Count > 0)
            UpdatedAt = nowUtc;

        return changes;
    }

    /// <summary>
    /// Spec 038 (US2 / D9) — "reviewed — no change" re-authorization for one
    /// regulatory field: refreshes that field's last-reviewed metadata without
    /// changing the value. Throws when the field's status is unset (re-authorizing
    /// "nothing" is meaningless). Returns a
    /// <see cref="RegulatoryChangeKind.ReviewedNoChange"/> record for auditing.
    /// </summary>
    public RegulatoryChange ConfirmRegulatoryReviewed(RegulatoryField field, string actorUserId, DateTime nowUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actorUserId);

        byte? code;
        switch (field)
        {
            case RegulatoryField.Hacienda:
                if (HaciendaStatus is null)
                    throw new InvalidOperationException("No se puede confirmar la revisión de un estado de Hacienda sin definir.");
                HaciendaLastReviewedAt = nowUtc;
                HaciendaLastReviewedBy = actorUserId;
                HaciendaLastReviewedSource = RegulatoryReviewSource.Manual;
                code = (byte?)HaciendaStatus;
                break;
            case RegulatoryField.Ccss:
                if (CcssStatus is null)
                    throw new InvalidOperationException("No se puede confirmar la revisión de un estado de CCSS sin definir.");
                CcssLastReviewedAt = nowUtc;
                CcssLastReviewedBy = actorUserId;
                CcssLastReviewedSource = RegulatoryReviewSource.Manual;
                code = (byte?)CcssStatus;
                break;
            case RegulatoryField.Sicop:
                if (SicopStatus is null)
                    throw new InvalidOperationException("No se puede confirmar la revisión de un estado de SICOP sin definir.");
                SicopLastReviewedAt = nowUtc;
                SicopLastReviewedBy = actorUserId;
                SicopLastReviewedSource = RegulatoryReviewSource.Manual;
                code = (byte?)SicopStatus;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(field));
        }

        UpdatedAt = nowUtc;
        // Safe cast: RegulatoryField and RegulatoryChangeField share identical
        // numeric codes for Hacienda=1/Ccss=2/Sicop=3 (the only values `field` can be).
        return new RegulatoryChange(
            (RegulatoryChangeField)field,
            code?.ToString(),
            code?.ToString(),
            RegulatoryChangeKind.ReviewedNoChange,
            RegulatoryReviewSource.Manual);
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
