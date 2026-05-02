using FundingPlatform.Application.Abstractions.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FundingPlatform.Infrastructure.Storage;

/// <summary>
/// FR-007: offline opt-in / test fallback. Maps <c>(category, key)</c> to
/// <c>{RootPath}/{container}/{rest-of-key}</c>. Atomic writes via
/// temp-file-then-rename. Throws <see cref="LocalProviderUrlNotSupportedException"/>
/// when a TimeLimitedUrl is requested.
/// </summary>
public sealed class LocalFilesystemObjectStorage : IObjectStorage
{
    private readonly ObjectStorageDiagnostics _diagnostics;
    private readonly StorageOptions _options;
    private readonly string _rootPath;

    public LocalFilesystemObjectStorage(
        ObjectStorageDiagnostics diagnostics,
        IOptions<StorageOptions> options,
        ILogger<LocalFilesystemObjectStorage> logger)
    {
        _diagnostics = diagnostics;
        _options = options.Value;
        _rootPath = _options.LocalFilesystem.RootPath
            ?? throw new InvalidOperationException(
                "Storage:Provider=LocalFilesystem requires Storage:LocalFilesystem:RootPath.");
        Directory.CreateDirectory(_rootPath);
    }

    public Task<StoredObject> UploadAsync(
        FileCategory category,
        ObjectKey key,
        Stream content,
        string contentType,
        long? contentLength,
        CancellationToken ct)
    {
        return _diagnostics.TrackAsync(
            "Upload",
            category,
            key,
            StorageProviderName.LocalFilesystem,
            async ctx =>
            {
                var path = ResolveAbsolutePath(key);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);

                var tempPath = path + ".tmp-" + Guid.NewGuid().ToString("N")[..8];
                long size;
                try
                {
                    await using (var stream = new FileStream(
                        tempPath,
                        FileMode.CreateNew,
                        FileAccess.Write,
                        FileShare.None))
                    {
                        await content.CopyToAsync(stream, ct).ConfigureAwait(false);
                        size = stream.Length;
                    }

                    File.Move(tempPath, path, overwrite: true);
                }
                catch
                {
                    if (File.Exists(tempPath))
                    {
                        try { File.Delete(tempPath); } catch { /* best-effort */ }
                    }
                    throw;
                }

                ctx.SizeBytes = size;
                return new StoredObject(
                    Container: key.Container,
                    Key: key.Value,
                    SizeBytes: size,
                    ContentType: contentType,
                    CreatedAt: File.GetCreationTimeUtc(path),
                    Provider: StorageProviderName.LocalFilesystem);
            },
            sizeBytes: contentLength,
            ct: ct);
    }

    public Task<Stream> OpenReadAsync(FileCategory category, ObjectKey key, CancellationToken ct)
    {
        return _diagnostics.TrackAsync(
            "Download",
            category,
            key,
            StorageProviderName.LocalFilesystem,
            ctx =>
            {
                var path = ResolveAbsolutePath(key);
                if (!File.Exists(path))
                    throw new ObjectNotFoundException(key.Container, key.Value);
                var info = new FileInfo(path);
                ctx.SizeBytes = info.Length;
                Stream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                return Task.FromResult(stream);
            },
            ct: ct);
    }

    public Task<bool> ExistsAsync(FileCategory category, ObjectKey key, CancellationToken ct)
    {
        return _diagnostics.TrackAsync(
            "Exists",
            category,
            key,
            StorageProviderName.LocalFilesystem,
            ctx => Task.FromResult(File.Exists(ResolveAbsolutePath(key))),
            ct: ct);
    }

    public Task DeleteAsync(FileCategory category, ObjectKey key, CancellationToken ct)
    {
        return _diagnostics.TrackAsync(
            "Delete",
            category,
            key,
            StorageProviderName.LocalFilesystem,
            ctx =>
            {
                var path = ResolveAbsolutePath(key);
                if (File.Exists(path))
                    File.Delete(path);
                return Task.CompletedTask;
            },
            ct: ct);
    }

    public Task<StorageHandle> ResolveServingHandleAsync(
        FileCategory category,
        ObjectKey key,
        ServingMode preferred,
        CancellationToken ct)
    {
        return _diagnostics.TrackAsync<StorageHandle>(
            "ResolveHandle",
            category,
            key,
            StorageProviderName.LocalFilesystem,
            ctx =>
            {
                if (preferred == ServingMode.TimeLimitedUrl)
                    throw new LocalProviderUrlNotSupportedException();

                var path = ResolveAbsolutePath(key);
                if (!File.Exists(path))
                    throw new ObjectNotFoundException(key.Container, key.Value);

                var info = new FileInfo(path);
                ctx.SizeBytes = info.Length;
                Stream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                StorageHandle handle = new BackendStreamHandle(
                    Content: stream,
                    ContentType: GuessContentType(key.Extension),
                    Length: info.Length);
                return Task.FromResult(handle);
            },
            ct: ct);
    }

    private string ResolveAbsolutePath(ObjectKey key)
    {
        var combined = Path.Combine(_rootPath, key.Value.Replace('/', Path.DirectorySeparatorChar));
        var rooted = Path.GetFullPath(combined);
        // Append a directory separator before the prefix check so a root of
        // "/data" doesn't accidentally match "/data2/..." (i.e. ensure the
        // resolved path is strictly *inside* the root, not just sharing the
        // root's leading characters).
        var rootedRoot = Path.GetFullPath(_rootPath);
        var rootedRootWithSep = rootedRoot.EndsWith(Path.DirectorySeparatorChar)
            ? rootedRoot
            : rootedRoot + Path.DirectorySeparatorChar;
        if (!rooted.Equals(rootedRoot, StringComparison.Ordinal) &&
            !rooted.StartsWith(rootedRootWithSep, StringComparison.Ordinal))
            throw new InvalidOperationException("Resolved path escaped the configured root.");
        return rooted;
    }

    private static string GuessContentType(string extension) => extension.ToLowerInvariant() switch
    {
        ".pdf" => "application/pdf",
        ".csv" => "text/csv",
        ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        ".xls" => "application/vnd.ms-excel",
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".txt" => "text/plain",
        _ => "application/octet-stream",
    };
}
