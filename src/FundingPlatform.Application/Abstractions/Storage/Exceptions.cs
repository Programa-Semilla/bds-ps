namespace FundingPlatform.Application.Abstractions.Storage;

/// <summary>Raised when the requested blob is absent (FR-edge "Missing blob on download").</summary>
public sealed class ObjectNotFoundException : Exception
{
    public string Container { get; }
    public string Key { get; }

    public ObjectNotFoundException(string container, string key)
        : base($"Object '{key}' not found in container '{container}'.")
    {
        Container = container;
        Key = key;
    }
}

/// <summary>Raised by the controller boundary when the upload exceeds the per-category cap (FR-022).</summary>
public sealed class OversizeException : Exception
{
    public FileCategory Category { get; }
    public long MaxSizeBytes { get; }
    public long ActualSizeBytes { get; }

    public OversizeException(FileCategory category, long maxSizeBytes, long actualSizeBytes)
        : base($"Upload to category '{category}' exceeded the {maxSizeBytes}-byte cap (actual: {actualSizeBytes}).")
    {
        Category = category;
        MaxSizeBytes = maxSizeBytes;
        ActualSizeBytes = actualSizeBytes;
    }
}

/// <summary>FR-edge "Local-mode parity gaps": LocalFilesystem cannot issue time-limited URLs.</summary>
public sealed class LocalProviderUrlNotSupportedException : Exception
{
    public LocalProviderUrlNotSupportedException()
        : base("LocalFilesystem provider does not support time-limited URLs. Use ServingMode.BackendStream.")
    {
    }
}

public enum ObjectStorageOperationReason
{
    RetryExhausted,
    Backend,
}

/// <summary>Wraps non-retryable SDK errors after the retry budget is exhausted.</summary>
public sealed class ObjectStorageOperationException : Exception
{
    public ObjectStorageOperationReason Reason { get; }
    public string? Operation { get; }
    public string? Container { get; }
    public string? Key { get; }

    public ObjectStorageOperationException(
        ObjectStorageOperationReason reason,
        string message,
        Exception? innerException = null,
        string? operation = null,
        string? container = null,
        string? key = null)
        : base(message, innerException)
    {
        Reason = reason;
        Operation = operation;
        Container = container;
        Key = key;
    }
}
