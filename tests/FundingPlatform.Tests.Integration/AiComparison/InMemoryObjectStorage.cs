using FundingPlatform.Application.Abstractions.Storage;

namespace FundingPlatform.Tests.Integration.AiComparison;

/// <summary>
/// Spec 020 — minimal <see cref="IObjectStorage"/> stub for orchestrator integration
/// tests. The orchestrator calls <c>OpenReadAsync</c> on every supplier blob; the
/// tests seed Documents with placeholder blob keys (e.g. "/store/a") that don't
/// match the canonical ObjectKey format. The orchestrator catches malformed keys
/// and skips the PDF block — so this stub only needs to satisfy the interface.
/// </summary>
internal sealed class InMemoryObjectStorage : IObjectStorage
{
    private readonly Dictionary<string, byte[]> _store = new();

    /// <summary>Number of stored blobs — lets tests assert orphan-cleanup (spec 036 / research D9).</summary>
    internal int StoredCount => _store.Count;

    public Task<StoredObject> UploadAsync(
        FileCategory category, ObjectKey key, Stream content, string contentType,
        long? contentLength, CancellationToken ct)
    {
        using var ms = new MemoryStream();
        content.CopyTo(ms);
        var bytes = ms.ToArray();
        _store[key.Value] = bytes;
        return Task.FromResult(new StoredObject(
            Container: key.Container,
            Key: key.Value,
            SizeBytes: bytes.LongLength,
            ContentType: contentType,
            CreatedAt: DateTimeOffset.UtcNow,
            Provider: StorageProviderName.LocalFilesystem));
    }

    public Task<Stream> OpenReadAsync(FileCategory category, ObjectKey key, CancellationToken ct)
    {
        if (!_store.TryGetValue(key.Value, out var bytes))
            throw new FileNotFoundException($"Key '{key.Value}' not seeded.");
        return Task.FromResult<Stream>(new MemoryStream(bytes, writable: false));
    }

    public Task<bool> ExistsAsync(FileCategory category, ObjectKey key, CancellationToken ct)
        => Task.FromResult(_store.ContainsKey(key.Value));

    public Task DeleteAsync(FileCategory category, ObjectKey key, CancellationToken ct)
    {
        _store.Remove(key.Value);
        return Task.CompletedTask;
    }

    public Task<StorageHandle> ResolveServingHandleAsync(
        FileCategory category, ObjectKey key, ServingMode preferred, CancellationToken ct)
    {
        if (!_store.TryGetValue(key.Value, out var bytes))
            throw new FileNotFoundException($"Key '{key.Value}' not seeded.");
        Stream content = new MemoryStream(bytes, writable: false);
        return Task.FromResult<StorageHandle>(
            new BackendStreamHandle(content, "application/pdf", bytes.LongLength));
    }
}
