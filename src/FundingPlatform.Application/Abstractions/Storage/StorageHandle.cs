namespace FundingPlatform.Application.Abstractions.Storage;

/// <summary>
/// Discriminated handle returned by <see cref="IObjectStorage.ResolveServingHandleAsync"/>.
/// </summary>
public abstract record StorageHandle;

public sealed record BackendStreamHandle(
    Stream Content,
    string ContentType,
    long? Length) : StorageHandle, IAsyncDisposable
{
    public ValueTask DisposeAsync() => Content.DisposeAsync();
}

public sealed record TimeLimitedUrlHandle(
    Uri Url,
    DateTimeOffset ExpiresAt,
    string ContentType,
    long? Length) : StorageHandle;

public enum ServingMode
{
    BackendStream,
    TimeLimitedUrl,
}
