namespace FundingPlatform.Application.Abstractions.Storage;

/// <summary>
/// Single storage abstraction (FR-001 / FR-002 / FR-003). Implementations are
/// selected at runtime via <c>Storage:Provider</c>. Authorization is the caller's
/// responsibility — this port performs none (FR-018).
/// </summary>
public interface IObjectStorage
{
    Task<StoredObject> UploadAsync(
        FileCategory category,
        ObjectKey key,
        Stream content,
        string contentType,
        long? contentLength,
        CancellationToken ct);

    Task<Stream> OpenReadAsync(
        FileCategory category,
        ObjectKey key,
        CancellationToken ct);

    Task<bool> ExistsAsync(
        FileCategory category,
        ObjectKey key,
        CancellationToken ct);

    Task DeleteAsync(
        FileCategory category,
        ObjectKey key,
        CancellationToken ct);

    Task<StorageHandle> ResolveServingHandleAsync(
        FileCategory category,
        ObjectKey key,
        ServingMode preferred,
        CancellationToken ct);
}
