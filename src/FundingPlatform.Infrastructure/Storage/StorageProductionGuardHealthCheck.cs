using FundingPlatform.Application.Abstractions.Storage;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FundingPlatform.Infrastructure.Storage;

/// <summary>
/// FR-011: when running in Production, a connection-string-based AzureBlob
/// configuration MUST log a warning and SHOULD be flagged so deployment gates
/// can fail. This health check returns Degraded in that case so an operator
/// notices.
/// </summary>
public sealed class StorageProductionGuardHealthCheck : IHealthCheck
{
    private readonly IOptions<StorageOptions> _options;
    private readonly IHostEnvironment _environment;
    private readonly ILogger<StorageProductionGuardHealthCheck> _logger;
    private bool _warningEmitted;

    public StorageProductionGuardHealthCheck(
        IOptions<StorageOptions> options,
        IHostEnvironment environment,
        ILogger<StorageProductionGuardHealthCheck> logger)
    {
        _options = options;
        _environment = environment;
        _logger = logger;
    }

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var opts = _options.Value;
        var isProduction = _environment.IsProduction();
        var isAzureBlob = string.Equals(opts.Provider, "AzureBlob", StringComparison.OrdinalIgnoreCase);
        var hasConnectionString = !string.IsNullOrWhiteSpace(opts.ConnectionString);

        if (isProduction && isAzureBlob && hasConnectionString)
        {
            if (!_warningEmitted)
            {
                _logger.LogWarning(
                    "Storage:Provider=AzureBlob in Production with a connection string configured. " +
                    "Production deployments MUST use managed identity (FR-011). Health check will report Degraded.");
                _warningEmitted = true;
            }

            return Task.FromResult(HealthCheckResult.Degraded(
                "Production AzureBlob storage configured with a connection string; managed identity is required (FR-011)."));
        }

        return Task.FromResult(HealthCheckResult.Healthy());
    }
}
