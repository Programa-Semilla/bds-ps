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
        // Spec 014 (T036) / FR-008 — when the test-fallback flag is enabled and
        // the configured provider is Azure-backed, opportunistically test the
        // connection string before binding options. If Azurite is unreachable
        // and the operator opted in, rewrite Provider to LocalFilesystem so the
        // suite still runs. Production envs never set this flag (validated by
        // StorageOptionsValidator + the production guard health check).
        var providerString = configuration[$"{StorageOptions.SectionName}:Provider"]
            ?? "Azurite";
        var allowFallback = string.Equals(
            configuration["Storage:TestFallback:AllowFilesystem"],
            "true",
            StringComparison.OrdinalIgnoreCase);

        var fellBackToFilesystem = false;
        if (allowFallback &&
            (string.Equals(providerString, "Azurite", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(providerString, "AzureBlob", StringComparison.OrdinalIgnoreCase)))
        {
            var connStr = configuration["Storage:ConnectionString"];
            if (!IsBlobEndpointReachable(connStr))
            {
                fellBackToFilesystem = true;
                // Switch to LocalFilesystem in-process. The AppHost has already
                // provisioned a temp root and surfaced it via
                // Storage__LocalFilesystem__RootPath when the operator set
                // Provider=LocalFilesystem outright; if we land here from a
                // failed Azurite probe, fall back to a fresh temp dir.
                var rootPath = configuration["Storage:LocalFilesystem:RootPath"];
                if (string.IsNullOrWhiteSpace(rootPath))
                {
                    rootPath = Path.Combine(
                        Path.GetTempPath(),
                        $"fundingplatform-storage-fallback-{Guid.NewGuid():N}");
                    Directory.CreateDirectory(rootPath);
                    if (configuration is IConfigurationRoot)
                    {
                        configuration["Storage:LocalFilesystem:RootPath"] = rootPath;
                    }
                }
                if (configuration is IConfigurationRoot)
                {
                    configuration["Storage:Provider"] = "LocalFilesystem";
                }
                providerString = "LocalFilesystem";
            }
        }

        services.AddOptions<StorageOptions>()
            .Bind(configuration.GetSection(StorageOptions.SectionName))
            .ValidateOnStart();

        services.AddSingleton<IValidateOptions<StorageOptions>, StorageOptionsValidator>();

        services.AddSingleton<ObjectStorageDiagnostics>();

        if (string.Equals(providerString, "LocalFilesystem", StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<IObjectStorage, LocalFilesystemObjectStorage>();
            if (fellBackToFilesystem)
            {
                // Surface the FR-008 fallback as a startup-time warning. Wired
                // through ILogger<StorageTestFallbackNotifier> so the message
                // ends up in the same sink as the rest of storage diagnostics.
                services.AddHostedService<StorageTestFallbackNotifier>();
            }
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

    /// <summary>
    /// Spec 014 (T036) / FR-008 — emits a single warning at startup when the
    /// registration fell back from Azure-backed storage to the local filesystem.
    /// </summary>
    internal sealed class StorageTestFallbackNotifier : Microsoft.Extensions.Hosting.IHostedService
    {
        private readonly ILogger<StorageTestFallbackNotifier> _logger;
        public StorageTestFallbackNotifier(ILogger<StorageTestFallbackNotifier> logger)
            => _logger = logger;
        public Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogWarning(
                "Storage:TestFallback:AllowFilesystem=true and the configured Azure-backed " +
                "endpoint was unreachable; falling back to LocalFilesystem provider. This " +
                "must NEVER happen outside test environments (FR-008).");
            return Task.CompletedTask;
        }
        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    /// <summary>
    /// Spec 014 (T036) / FR-008 — quick TCP-level reachability probe for the
    /// blob endpoint. Avoids a 60s SDK retry storm when Azurite is down. We
    /// don't authenticate; we just confirm the host:port answers. A null or
    /// empty connection string is treated as "not reachable" so the caller
    /// proceeds to the LocalFilesystem fallback.
    /// </summary>
    private static bool IsBlobEndpointReachable(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            return false;

        Uri? endpoint = null;
        foreach (var part in connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = part.IndexOf('=');
            if (eq <= 0) continue;
            var k = part[..eq].Trim();
            var v = part[(eq + 1)..].Trim();
            if (string.Equals(k, "BlobEndpoint", StringComparison.OrdinalIgnoreCase) &&
                Uri.TryCreate(v, UriKind.Absolute, out var u))
            {
                endpoint = u;
                break;
            }
        }
        if (endpoint is null) return false;

        try
        {
            using var client = new System.Net.Sockets.TcpClient();
            var task = client.ConnectAsync(endpoint.Host, endpoint.Port);
            return task.Wait(TimeSpan.FromSeconds(2)) && client.Connected;
        }
        catch
        {
            return false;
        }
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
