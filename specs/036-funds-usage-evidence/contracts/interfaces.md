# Contract: Domain & Application Interfaces

## Domain — `FundsUsageEvidence`

```csharp
public sealed class FundsUsageEvidence
{
    public int Id { get; private set; }
    public int ApplicationId { get; private set; }
    public string UploadedByUserId { get; private set; }
    public string OriginalFileName { get; private set; }
    public string BlobKey { get; private set; }
    public long FileSize { get; private set; }
    public string ContentType { get; private set; }
    public string? Note { get; private set; }
    public DateTime UploadedAt { get; private set; }
    public byte[] RowVersion { get; private set; }

    private FundsUsageEvidence() { } // EF

    // Enforces the AgreementExecuted gate (FR-001) + field/length invariants (FR-006).
    public static FundsUsageEvidence CreateForExecutedApplication(
        Application application, string uploadedByUserId, string originalFileName,
        string blobKey, long fileSize, string contentType, string? note);

    public void EditNote(string? note); // ≤250 (FR-006); empty → null
}
```

## Application — `IFundsUsageEvidenceService`

```csharp
namespace FundingPlatform.Application.FundsUsageEvidence;

public interface IFundsUsageEvidenceService
{
    // Flat, group-scoped read (no Application aggregate hydration). Returns rows ordered newest-first.
    Task<IReadOnlyList<FundsUsageEvidenceListItem>> ListAsync(int applicationId, CancellationToken ct);

    // Validates the application is AgreementExecuted (via the domain factory), stores the blob,
    // persists the row + audit in one transaction. Returns the created id.
    Task<int> UploadAsync(UploadFundsUsageEvidenceCommand cmd, string actorUserId, CancellationToken ct);

    Task EditNoteAsync(int evidenceId, string? note, string actorUserId, CancellationToken ct);

    // Deletes the blob then the row + audit. Idempotent: missing row → throws KeyNotFoundException
    // (controller maps to NotFound()).
    Task DeleteAsync(int evidenceId, string actorUserId, CancellationToken ct);

    // Resolves a BackendStream serving handle for download.
    Task<FundsUsageEvidenceDownload?> OpenForDownloadAsync(int evidenceId, CancellationToken ct);
}
```

DTOs (`FundsUsageEvidenceDtos.cs`):

```csharp
public sealed record UploadFundsUsageEvidenceCommand(
    int ApplicationId, string OriginalFileName, string ContentType,
    long FileSize, Stream Content, string? Note);

public sealed record FundsUsageEvidenceListItem(
    int Id, string OriginalFileName, string? Note,
    string UploadedByDisplayName, DateTime UploadedAt, long FileSize, string ContentType);

public sealed record FundsUsageEvidenceDownload(
    Stream Content, string ContentType, string FileName);
```

Note: the service receives an already **type-validated, size-bounded** stream — the controller runs
`UploadSizeGuard` (size) and `EvidenceFileTypePolicy` (type + magic bytes) at the boundary before calling
`UploadAsync`. Group-scope authorization is enforced in the controller (it has the HTTP principal/scope);
the service trusts the caller for scope, exactly like existing services.

## Application — `EvidenceFileTypePolicy` (pure, unit-testable)

```csharp
namespace FundingPlatform.Application.FundsUsageEvidence;

public static class EvidenceFileTypePolicy
{
    // Returns true if (extension ∈ allow-list) AND (declared content-type consistent)
    // AND (head bytes match the family's magic). `head` is the first N buffered bytes.
    public static bool IsAllowed(string fileName, string? declaredContentType, ReadOnlySpan<byte> head);

    // The canonical es-CR-friendly allowed-extension list for the accept hint + tests.
    public static IReadOnlyList<string> AllowedExtensions { get; } // .pdf .png .jpg .jpeg .webp .heic .heif .docx .doc .xlsx .xls
}
```

Allowed families and magic bytes per research D3.

## Infrastructure — repository

```csharp
public interface IFundsUsageEvidenceRepository
{
    Task<IReadOnlyList<FundsUsageEvidence>> ListByApplicationAsync(int applicationId, CancellationToken ct);
    Task<FundsUsageEvidence?> GetAsync(int evidenceId, CancellationToken ct);
    Task AddAsync(FundsUsageEvidence evidence, CancellationToken ct);
    void Remove(FundsUsageEvidence evidence);
    // SaveChanges is orchestrated by the service so audit + mutation commit together.
}
```
(May be folded into the service using `AppDbContext` directly, consistent with how some existing
services access the context — pin during implementation.)
