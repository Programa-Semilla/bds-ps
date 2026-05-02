namespace FundingPlatform.Application.Abstractions.Storage;

/// <summary>
/// Result of a successful upload. Read by callers (to persist <see cref="Key"/> on
/// the owning aggregate) and emitted in diagnostics (FR-025).
/// </summary>
public sealed record StoredObject(
    string Container,
    string Key,
    long SizeBytes,
    string ContentType,
    DateTimeOffset CreatedAt,
    StorageProviderName Provider);

/// <summary>Distinguishes the runtime backend (FR-025 'provider' log field).</summary>
public enum StorageProviderName
{
    AzureBlob,
    Azurite,
    LocalFilesystem,
}
