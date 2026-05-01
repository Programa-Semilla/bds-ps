using Azure.Identity;
using Azure.Storage.Blobs;
using FundingPlatform.Application.Abstractions.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FundingPlatform.Infrastructure.Storage;

/// <summary>
/// FR-004 / FR-012: registers the storage abstraction and the right backend
/// implementation based on <c>Storage:Provider</c>. Fails fast on misconfiguration.
/// </summary>
public static class ObjectStorageRegistration
{
    public static IServiceCollection AddObjectStorage(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<StorageOptions>()
            .Bind(configuration.GetSection(StorageOptions.SectionName))
            .ValidateOnStart();

        services.AddSingleton<IValidateOptions<StorageOptions>, StorageOptionsValidator>();

        services.AddSingleton<ObjectStorageDiagnostics>();

        var providerString = configuration[$"{StorageOptions.SectionName}:Provider"]
            ?? configuration["Storage:Provider"]
            ?? "Azurite";

        if (string.Equals(providerString, "LocalFilesystem", StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<IObjectStorage, LocalFilesystemObjectStorage>();
        }
        else if (
            string.Equals(providerString, "AzureBlob", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(providerString, "Azurite", StringComparison.OrdinalIgnoreCase))
        {
            // Register a fallback BlobServiceClient when one wasn't registered by
            // Aspire's AddAzureBlobClient. The TryAdd preserves Aspire-provided client
            // (with OTel + health checks) when present, e.g. Web project.
            services.TryAddSingleton<BlobServiceClient>(sp =>
            {
                var opts = sp.GetRequiredService<IOptions<StorageOptions>>().Value;
                return BuildBlobServiceClient(opts, sp.GetRequiredService<ILogger<BlobServiceClient>>());
            });

            services.AddSingleton<IObjectStorage, AzureBlobObjectStorage>();
            services.AddHostedService<EnsureContainersHostedService>();

            // FR-011 production guard.
            services.AddHealthChecks()
                .AddCheck<StorageProductionGuardHealthCheck>(
                    "storage-production-guard",
                    failureStatus: HealthStatus.Degraded);
        }
        else
        {
            throw new InvalidOperationException(
                $"Unknown Storage:Provider '{providerString}'. Valid: AzureBlob, Azurite, LocalFilesystem.");
        }

        return services;
    }

    internal static BlobServiceClient BuildBlobServiceClient(StorageOptions opts, ILogger logger)
    {
        // Honor an explicit ConnectionString first (Azurite + lower envs).
        if (!string.IsNullOrWhiteSpace(opts.ConnectionString))
        {
            logger.LogDebug("Storage: building BlobServiceClient from connection string.");
            return new BlobServiceClient(opts.ConnectionString);
        }

        if (!string.IsNullOrWhiteSpace(opts.AccountReference))
        {
            // AccountReference is the storage account URI (https://{account}.blob.core.windows.net/).
            // Use DefaultAzureCredential — managed identity preferred chain.
            logger.LogDebug("Storage: building BlobServiceClient from account reference {Account} via DefaultAzureCredential.", opts.AccountReference);
            return new BlobServiceClient(new Uri(opts.AccountReference), new DefaultAzureCredential());
        }

        throw new InvalidOperationException(
            "Storage:Provider requires either Storage:ConnectionString or Storage:AccountReference. Neither was provided.");
    }
}
