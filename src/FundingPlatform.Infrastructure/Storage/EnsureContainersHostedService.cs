using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using FundingPlatform.Application.Abstractions.Storage;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FundingPlatform.Infrastructure.Storage;

/// <summary>
/// FR-013: ensure the four well-known containers exist on Web app startup.
/// FR-016 / FR-027: containers are created private (no public access) and the
/// hosted service refuses to start if any existing container has anonymous
/// access enabled.
/// </summary>
public sealed class EnsureContainersHostedService : IHostedService
{
    private readonly BlobServiceClient _serviceClient;
    private readonly StorageOptions _options;
    private readonly ILogger<EnsureContainersHostedService> _logger;

    public EnsureContainersHostedService(
        BlobServiceClient serviceClient,
        IOptions<StorageOptions> options,
        ILogger<EnsureContainersHostedService> logger)
    {
        _serviceClient = serviceClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // Skip when running with LocalFilesystem provider (no containers).
        if (string.Equals(_options.Provider, "LocalFilesystem", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogDebug("Storage provider is LocalFilesystem; skipping container bootstrap.");
            return;
        }

        foreach (var name in FileCategoryExtensions.AllContainerNames)
        {
            var container = _serviceClient.GetBlobContainerClient(name);
            try
            {
                await container.CreateIfNotExistsAsync(
                    publicAccessType: PublicAccessType.None,
                    cancellationToken: cancellationToken).ConfigureAwait(false);

                // FR-027: verify the container is not publicly accessible.
                // PublicAccessType.None is the safe baseline. Azurite reports
                // PublicAccessType.None for emulator-created containers; on
                // real Azure, only Blob and Container values are unsafe — any
                // other (None, or older SDKs reporting an unknown sentinel)
                // is treated as private.
                var access = await container.GetAccessPolicyAsync(cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                var publicAccess = access.Value.BlobPublicAccess;
                if (publicAccess == PublicAccessType.Blob ||
                    publicAccess == PublicAccessType.BlobContainer)
                {
                    throw new InvalidOperationException(
                        $"Container '{name}' has public access enabled ({publicAccess}). " +
                        "Disable anonymous access before starting the platform (FR-027).");
                }
            }
            catch (Azure.RequestFailedException ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to ensure container {Container} exists/private. The platform requires the four canonical containers (FR-013) to be present and private (FR-016, FR-027).",
                    name);
                throw;
            }
        }

        _logger.LogInformation(
            "Storage container bootstrap complete: {Containers}.",
            string.Join(", ", FileCategoryExtensions.AllContainerNames));
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
