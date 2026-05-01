using FundingPlatform.Application.Abstractions.Storage;
using FundingPlatform.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace FundingPlatform.Infrastructure.Storage.Legacy;

/// <summary>
/// Spec 014 transition adapter: bridges the legacy <see cref="IFileStorageService"/>
/// shape to <see cref="IObjectStorage"/> while individual call sites are being
/// migrated. Stored "paths" returned from this facade are canonical
/// <see cref="ObjectKey"/> values prefixed with the container name. Deleted in
/// T053 once every caller is on <see cref="IObjectStorage"/> directly.
///
/// Because the legacy interface is category-agnostic, every write through this
/// facade lands in <see cref="FileCategory.GeneratedArtifact"/> and emits a
/// warning so silent miscategorisation is observable until the controllers
/// migrate (T028 / T052).
/// </summary>
public sealed class FileStorageServiceFacade : IFileStorageService
{
    private const FileCategory DefaultCategory = FileCategory.GeneratedArtifact;
    private const string AdminOwnerSegment = "admin";
    private const string EntityIdAnonymous = "legacy";

    private readonly IObjectStorage _objectStorage;
    private readonly ILogger<FileStorageServiceFacade> _logger;

    public FileStorageServiceFacade(
        IObjectStorage objectStorage,
        ILogger<FileStorageServiceFacade> logger)
    {
        _objectStorage = objectStorage;
        _logger = logger;
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

        _logger.LogWarning(
            "FileStorageServiceFacade.SaveFileAsync routing '{FileName}' to category {Category}. " +
            "This is a transitional adapter (spec 014 / T028 / T052); the caller should migrate to IObjectStorage " +
            "with the correct FileCategory so signed PDFs and supplier imports are not stored under generated-artifacts.",
            fileName,
            DefaultCategory);

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
        // owner of pre-014 file lifecycle. Emit a warning so the no-op is
        // observable in operator logs.
        if (!LooksLikeObjectKey(storagePath))
        {
            _logger.LogWarning(
                "FileStorageServiceFacade.DeleteFileAsync skipped non-canonical path '{Path}'. " +
                "Pre-014 filesystem paths are owned by the storage-migration tool (T040); " +
                "this delete is a no-op until the path is migrated.",
                storagePath);
            return Task.CompletedTask;
        }

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
