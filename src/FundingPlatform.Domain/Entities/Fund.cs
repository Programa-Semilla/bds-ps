// Spec 029 — see specs/029-fund-entity/data-model.md (Fund aggregate) and research D1.

using FundingPlatform.Domain.Enums;

namespace FundingPlatform.Domain.Entities;

/// <summary>
/// Spec 029 / FR-001 — top-level lifecycle aggregate above <see cref="Process"/>
/// (<c>Fund → Process → Group → Application</c>). A Fund (Fondo) carries a Name,
/// Description, Active/Archived status, and an optional applicant-downloadable
/// regulation PDF (single document, stored via spec-014 IObjectStorage with the
/// blob key + metadata held as columns on this aggregate, mirroring
/// <see cref="FundingAgreement"/>).
///
/// Archiving force-freezes every anchored application (research D6): excluded
/// from non-admin reads and read-only against mutation. The Fund itself stays
/// editable only through the lifecycle methods while Archived.
/// </summary>
public class Fund
{
    public const int MaxNameLength = 120;
    public const int MaxDescriptionLength = 2000;

    private readonly List<Process> _processes = [];

    public int Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public FundStatus Status { get; private set; }

    // Spec 029 / D3 — single optional regulation reference. All-or-nothing.
    public string? RegulationBlobKey { get; private set; }
    public string? RegulationFileName { get; private set; }
    public string? RegulationContentType { get; private set; }
    public long? RegulationSizeBytes { get; private set; }
    public DateTime? RegulationUploadedAtUtc { get; private set; }
    public string? RegulationUploadedByUserId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public IReadOnlyCollection<Process> Processes => _processes.AsReadOnly();

    /// <summary>True when a regulation document is attached.</summary>
    public bool HasRegulation => RegulationBlobKey is not null;

    private Fund() { }

    private Fund(string name, string description, DateTimeOffset now)
    {
        Name = name;
        Description = description;
        Status = FundStatus.Active;
        CreatedAt = now;
    }

    /// <summary>
    /// Factory: a new Fund is always created Active. Catalog uniqueness on Name
    /// is enforced by the DB unique index <c>UX_Funds_Name</c>; the service
    /// pre-checks for a friendly es-CR message.
    /// </summary>
    public static Fund Create(string name, string description)
        => new(ValidateName(name), ValidateDescription(description), DateTimeOffset.UtcNow);

    /// <summary>Renames the Fund. Guarded against Archived (lifecycle-only while archived).</summary>
    public void Rename(string newName)
    {
        EnsureActive();
        var trimmed = ValidateName(newName);
        if (!string.Equals(trimmed, Name, StringComparison.Ordinal))
        {
            Name = trimmed;
        }
    }

    /// <summary>Edits the description. Guarded against Archived.</summary>
    public void EditDescription(string newDescription)
    {
        EnsureActive();
        Description = ValidateDescription(newDescription);
    }

    /// <summary>
    /// Transitions Active → Archived. Idempotent: re-archiving an already
    /// Archived Fund is a no-op (no exception). Freeze takes effect for every
    /// anchored application via the read filter + mutation guards.
    /// </summary>
    public void Archive()
    {
        if (Status == FundStatus.Archived)
        {
            return;
        }
        Status = FundStatus.Archived;
    }

    /// <summary>Transitions Archived → Active. Idempotent.</summary>
    public void Reactivate()
    {
        if (Status == FundStatus.Active)
        {
            return;
        }
        Status = FundStatus.Active;
    }

    /// <summary>
    /// Sets or replaces the regulation reference (the caller has already stored
    /// the blob and is responsible for deleting any superseded blob). All
    /// columns are set together. Permitted regardless of status so an admin can
    /// curate documents (lifecycle of the regulation is independent of freeze).
    /// </summary>
    public void SetRegulation(
        string blobKey,
        string fileName,
        string contentType,
        long sizeBytes,
        string uploadedByUserId,
        DateTime nowUtc)
    {
        if (string.IsNullOrWhiteSpace(blobKey))
        {
            throw new ArgumentException("Regulation blob key is required.", nameof(blobKey));
        }
        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw new ArgumentException("Regulation file name is required.", nameof(fileName));
        }
        if (string.IsNullOrWhiteSpace(contentType))
        {
            throw new ArgumentException("Regulation content type is required.", nameof(contentType));
        }
        if (sizeBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sizeBytes), sizeBytes, "Regulation size must be positive.");
        }
        if (string.IsNullOrWhiteSpace(uploadedByUserId))
        {
            throw new ArgumentException("Uploader user id is required.", nameof(uploadedByUserId));
        }

        RegulationBlobKey = blobKey;
        RegulationFileName = fileName;
        RegulationContentType = contentType;
        RegulationSizeBytes = sizeBytes;
        RegulationUploadedAtUtc = nowUtc;
        RegulationUploadedByUserId = uploadedByUserId;
    }

    /// <summary>
    /// Clears the regulation reference (the caller deletes the blob). All
    /// columns are cleared together. No-op when no regulation is attached.
    /// </summary>
    public void RemoveRegulation()
    {
        RegulationBlobKey = null;
        RegulationFileName = null;
        RegulationContentType = null;
        RegulationSizeBytes = null;
        RegulationUploadedAtUtc = null;
        RegulationUploadedByUserId = null;
    }

    private void EnsureActive()
    {
        if (Status == FundStatus.Archived)
        {
            throw new InvalidOperationException(
                "No se puede editar un fondo archivado. Reactívelo primero.");
        }
    }

    private static string ValidateName(string name)
    {
        if (name is null)
        {
            throw new ArgumentException("Fund name is required.", nameof(name));
        }
        var trimmed = name.Trim();
        if (trimmed.Length == 0)
        {
            throw new ArgumentException("Fund name is required.", nameof(name));
        }
        if (trimmed.Length > MaxNameLength)
        {
            throw new ArgumentException(
                $"Fund name must be {MaxNameLength} characters or fewer.", nameof(name));
        }
        return trimmed;
    }

    private static string ValidateDescription(string description)
    {
        if (description is null)
        {
            throw new ArgumentException("Fund description is required.", nameof(description));
        }
        var trimmed = description.Trim();
        if (trimmed.Length == 0)
        {
            throw new ArgumentException("Fund description is required.", nameof(description));
        }
        if (trimmed.Length > MaxDescriptionLength)
        {
            throw new ArgumentException(
                $"Fund description must be {MaxDescriptionLength} characters or fewer.", nameof(description));
        }
        return trimmed;
    }
}
