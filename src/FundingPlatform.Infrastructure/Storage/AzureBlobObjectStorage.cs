using Azure;
using Azure.Storage;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;
using FundingPlatform.Application.Abstractions.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FundingPlatform.Infrastructure.Storage;

/// <summary>
/// FR-002 / FR-003 / FR-005 / FR-006 implementation backed by the Azure Storage
/// SDK. Same code path runs against Azurite (development emulator) and a real
/// Azure Storage account; the resolved endpoint determines the
/// <see cref="StorageProviderName"/> reported in diagnostics.
/// </summary>
public sealed class AzureBlobObjectStorage : IObjectStorage
{
    private readonly BlobServiceClient _serviceClient;
    private readonly ObjectStorageDiagnostics _diagnostics;
    private readonly StorageOptions _options;
    private readonly ILogger<AzureBlobObjectStorage> _logger;
    private readonly StorageProviderName _provider;

    public AzureBlobObjectStorage(
        BlobServiceClient serviceClient,
        ObjectStorageDiagnostics diagnostics,
        IOptions<StorageOptions> options,
        ILogger<AzureBlobObjectStorage> logger)
    {
        _serviceClient = serviceClient;
        _diagnostics = diagnostics;
        _options = options.Value;
        _logger = logger;
        _provider = ResolveProvider(_serviceClient.Uri);
    }

    public StorageProviderName Provider => _provider;

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
            _provider,
            async ctx =>
            {
                var container = _serviceClient.GetBlobContainerClient(key.Container);
                var blob = container.GetBlobClient(BlobNameFromKey(key));

                var transfer = new StorageTransferOptions
                {
                    InitialTransferSize = (int)Math.Max(_options.StreamingThresholdBytes, 1 * 1024 * 1024),
                    MaximumTransferSize = 4 * 1024 * 1024,
                    MaximumConcurrency = 1,
                };

                var uploadOptions = new BlobUploadOptions
                {
                    HttpHeaders = new BlobHttpHeaders { ContentType = contentType },
                    TransferOptions = transfer,
                };

                try
                {
                    var response = await blob.UploadAsync(content, uploadOptions, ct).ConfigureAwait(false);
                    var props = await blob.GetPropertiesAsync(cancellationToken: ct).ConfigureAwait(false);
                    ctx.SizeBytes = props.Value.ContentLength;

                    return new StoredObject(
                        Container: key.Container,
                        Key: key.Value,
                        SizeBytes: props.Value.ContentLength,
                        ContentType: contentType,
                        CreatedAt: response.Value.LastModified == default
                            ? DateTimeOffset.UtcNow
                            : response.Value.LastModified,
                        Provider: _provider);
                }
                catch (RequestFailedException ex) when (IsRetryExhausted(ex))
                {
                    throw new ObjectStorageOperationException(
                        ObjectStorageOperationReason.RetryExhausted,
                        $"Upload to {key.Container}/{key.Value} exhausted retry budget.",
                        ex,
                        operation: "Upload",
                        container: key.Container,
                        key: key.Value);
                }
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
            _provider,
            async ctx =>
            {
                var container = _serviceClient.GetBlobContainerClient(key.Container);
                var blob = container.GetBlobClient(BlobNameFromKey(key));
                try
                {
                    var response = await blob.DownloadStreamingAsync(cancellationToken: ct).ConfigureAwait(false);
                    ctx.SizeBytes = response.Value.Details.ContentLength;
                    return response.Value.Content;
                }
                catch (RequestFailedException ex) when (ex.Status == 404)
                {
                    throw new ObjectNotFoundException(key.Container, key.Value);
                }
                catch (RequestFailedException ex) when (IsRetryExhausted(ex))
                {
                    throw new ObjectStorageOperationException(
                        ObjectStorageOperationReason.RetryExhausted,
                        $"Download from {key.Container}/{key.Value} exhausted retry budget.",
                        ex,
                        operation: "Download",
                        container: key.Container,
                        key: key.Value);
                }
            },
            ct: ct);
    }

    public Task<bool> ExistsAsync(FileCategory category, ObjectKey key, CancellationToken ct)
    {
        return _diagnostics.TrackAsync(
            "Exists",
            category,
            key,
            _provider,
            async ctx =>
            {
                var container = _serviceClient.GetBlobContainerClient(key.Container);
                var blob = container.GetBlobClient(BlobNameFromKey(key));
                var response = await blob.ExistsAsync(ct).ConfigureAwait(false);
                return response.Value;
            },
            ct: ct);
    }

    public Task DeleteAsync(FileCategory category, ObjectKey key, CancellationToken ct)
    {
        return _diagnostics.TrackAsync(
            "Delete",
            category,
            key,
            _provider,
            async ctx =>
            {
                var container = _serviceClient.GetBlobContainerClient(key.Container);
                var blob = container.GetBlobClient(BlobNameFromKey(key));
                await blob.DeleteIfExistsAsync(cancellationToken: ct).ConfigureAwait(false);
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
            _provider,
            async ctx =>
            {
                var container = _serviceClient.GetBlobContainerClient(key.Container);
                var blob = container.GetBlobClient(BlobNameFromKey(key));

                var existsResponse = await blob.ExistsAsync(ct).ConfigureAwait(false);
                if (!existsResponse.Value)
                    throw new ObjectNotFoundException(key.Container, key.Value);

                if (preferred == ServingMode.TimeLimitedUrl)
                {
                    var categoryOptions = _options.Categories.For(category);
                    var expiry = TimeSpan.FromSeconds(Math.Min(
                        categoryOptions.UrlExpirySeconds,
                        StorageOptions.MaxUrlExpirySeconds));
                    var expiresAt = DateTimeOffset.UtcNow.Add(expiry);

                    if (!blob.CanGenerateSasUri)
                    {
                        // Fallback to backend stream when SAS isn't possible (e.g., DefaultAzureCredential
                        // without user-delegation key permissions).
                        _logger.LogDebug(
                            "BlobClient cannot generate SAS for {Container}/{Key}; falling back to backend stream.",
                            key.Container,
                            key.Value);
                    }
                    else
                    {
                        var sasBuilder = new BlobSasBuilder(BlobSasPermissions.Read, expiresAt)
                        {
                            BlobContainerName = key.Container,
                            BlobName = BlobNameFromKey(key),
                            Resource = "b",
                        };
                        var sasUri = blob.GenerateSasUri(sasBuilder);
                        var props = await blob.GetPropertiesAsync(cancellationToken: ct).ConfigureAwait(false);
                        ctx.SizeBytes = props.Value.ContentLength;
                        return new TimeLimitedUrlHandle(
                            Url: sasUri,
                            ExpiresAt: expiresAt,
                            ContentType: props.Value.ContentType ?? "application/octet-stream",
                            Length: props.Value.ContentLength);
                    }
                }

                var dl = await blob.DownloadStreamingAsync(cancellationToken: ct).ConfigureAwait(false);
                ctx.SizeBytes = dl.Value.Details.ContentLength;
                return new BackendStreamHandle(
                    Content: dl.Value.Content,
                    ContentType: dl.Value.Details.ContentType ?? "application/octet-stream",
                    Length: dl.Value.Details.ContentLength);
            },
            ct: ct);
    }

    private static string BlobNameFromKey(ObjectKey key)
    {
        // The container is the first segment of key.Value; the rest is the blob name.
        var firstSlash = key.Value.IndexOf('/');
        return firstSlash >= 0 ? key.Value[(firstSlash + 1)..] : key.Value;
    }

    private static StorageProviderName ResolveProvider(Uri endpoint)
    {
        // Azurite runs at 127.0.0.1, localhost, or *.azurite.* (Aspire emulator).
        // Real Azure storage uses *.blob.core.windows.net.
        var host = endpoint.Host;
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
            host == "127.0.0.1" ||
            host.Contains("azurite", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".local", StringComparison.OrdinalIgnoreCase))
        {
            return StorageProviderName.Azurite;
        }
        return StorageProviderName.AzureBlob;
    }

    private static bool IsRetryExhausted(RequestFailedException ex)
    {
        // 5xx after retry budget exhaustion or transient network errors that
        // bubble up are considered retry-exhausted. The SDK already retries
        // transient failures internally up to its policy; reaching here means
        // the policy gave up.
        return ex.Status == 0 || ex.Status >= 500;
    }
}
