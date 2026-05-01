using FundingPlatform.Application.Abstractions.Storage;
using FundingPlatform.Infrastructure.Storage;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FundingPlatform.Tests.Integration.Storage;

/// <summary>T034 — FR-011 production guard.</summary>
[TestFixture]
public class ProductionGuardTests
{
    private static StorageProductionGuardHealthCheck Build(string env, string provider, string? connStr)
    {
        var opts = new StorageOptions { Provider = provider, ConnectionString = connStr };
        var hostEnv = new HostEnvironmentStub(env);
        return new StorageProductionGuardHealthCheck(
            Options.Create(opts),
            hostEnv,
            NullLogger<StorageProductionGuardHealthCheck>.Instance);
    }

    [Test]
    public async Task Production_with_connection_string_returns_Degraded()
    {
        var hc = Build("Production", "AzureBlob", "DefaultEndpointsProtocol=https;...");
        var result = await hc.CheckHealthAsync(new HealthCheckContext());
        Assert.That(result.Status, Is.EqualTo(HealthStatus.Degraded));
    }

    [Test]
    public async Task Production_with_managed_identity_returns_Healthy()
    {
        var hc = Build("Production", "AzureBlob", null);
        var result = await hc.CheckHealthAsync(new HealthCheckContext());
        Assert.That(result.Status, Is.EqualTo(HealthStatus.Healthy));
    }

    [Test]
    public async Task Development_with_connection_string_returns_Healthy()
    {
        var hc = Build("Development", "AzureBlob", "DefaultEndpointsProtocol=https;...");
        var result = await hc.CheckHealthAsync(new HealthCheckContext());
        Assert.That(result.Status, Is.EqualTo(HealthStatus.Healthy));
    }

    [Test]
    public async Task LocalFilesystem_provider_in_production_returns_Healthy()
    {
        // The guard only flags AzureBlob+connection-string in Production. LocalFilesystem
        // is its own concern (FR-005 / FR-026 — operator runbook).
        var hc = Build("Production", "LocalFilesystem", null);
        var result = await hc.CheckHealthAsync(new HealthCheckContext());
        Assert.That(result.Status, Is.EqualTo(HealthStatus.Healthy));
    }

    private sealed class HostEnvironmentStub : IHostEnvironment
    {
        public HostEnvironmentStub(string env) => EnvironmentName = env;
        public string ApplicationName { get; set; } = "Tests";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
        public string ContentRootPath { get; set; } = string.Empty;
        public string EnvironmentName { get; set; }
    }
}
