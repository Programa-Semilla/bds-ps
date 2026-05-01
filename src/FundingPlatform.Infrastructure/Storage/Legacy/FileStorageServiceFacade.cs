using FundingPlatform.Application.Abstractions.Storage;
using FundingPlatform.Domain.Interfaces;

namespace FundingPlatform.Infrastructure.Storage.Legacy;

/// <summary>
/// Spec 014 transition adapter: bridges the legacy <see cref="IFileStorageService"/>
/// shape to <see cref="IObjectStorage"/> while individual call sites are being
/// migrated. Stored "paths" returned from this facade are canonical
/// <see cref="ObjectKey"/> values prefixed with the container name. Deleted in
/// T053 once every caller is on <see cref="IObjectStorage"/> directly.
/// </summary>
public sealed class FileStorageServiceFacade : IFileStorageService
{
    private const FileCategory DefaultCategory = FileCategory.GeneratedArtifact;
    private const string AdminOwnerSegment = "admin";
    private const string EntityIdAnonymous = "legacy";

    private readonly IObjectStorage _objectStorage;

    public FileStorageServiceFacade(IObjectStorage objectStorage)
    {
        _objectStorage = objectStorage;
    }

    public async Task<string> SaveFileAsync(Stream fileStream, string fileName, string contentType)
    {
        var suffix = Guid.NewGuid().ToString("N")[..16];
        var ext = Path.GetExtension(fileName);
        var key = ObjectKey.Build(
            DefaultCategory,
            AdminOwnerSegment,
            EntityIdAnonymous,
            suffix,
            ext);

        var stored = await _objectStorage.UploadAsync(
            DefaultCategory,
            key,
            fileStream,
            contentType,
            null,
            CancellationToken.None).ConfigureAwait(false);

        return stored.Key;
    }

    public Task DeleteFileAsync(string storagePath)
    {
        // Path may be a legacy absolute filesystem path (pre-014 data) OR a
        // canonical key. We can only delete canonical keys via the abstraction;
        // legacy paths are left alone — the migration tool (T040) is the
        // owner of pre-014 file lifecycle.
        if (!LooksLikeObjectKey(storagePath))
            return Task.CompletedTask;

        var key = ObjectKey.Parse(storagePath);
        var category = ResolveCategory(key);
        return _objectStorage.DeleteAsync(category, key, CancellationToken.None);
    }

    public async Task<Stream> GetFileAsync(string storagePath)
    {
        if (!LooksLikeObjectKey(storagePath))
            throw new FileNotFoundException(
                $"Legacy storage path '{storagePath}' is not a canonical object key. " +
                "Run the storage migration tool (T040) before attempting to read this file.",
                storagePath);

        var key = ObjectKey.Parse(storagePath);
        var category = ResolveCategory(key);
        return await _objectStorage.OpenReadAsync(category, key, CancellationToken.None).ConfigureAwait(false);
    }

    private static bool LooksLikeObjectKey(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return false;
        if (raw.StartsWith('/') || raw.Contains(":\\")) return false; // absolute filesystem
        return raw.Contains('/') && raw == raw.ToLowerInvariant();
    }

    private static FileCategory ResolveCategory(ObjectKey key) => key.Container switch
    {
        "signed-funding-agreements" => FileCategory.SignedFundingAgreement,
        "supplier-catalog-imports" => FileCategory.SupplierCatalogImport,
        "application-attachments" => FileCategory.ApplicationAttachment,
        "generated-artifacts" => FileCategory.GeneratedArtifact,
        _ => FileCategory.GeneratedArtifact,
    };
}
