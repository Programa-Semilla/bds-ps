using FundingPlatform.Application.Abstractions.Storage;
using FundingPlatform.Infrastructure.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace FundingPlatform.Tests.Integration.Storage;

/// <summary>
/// Spec 014 / T038 / FR-008 — when the operator opts into
/// <c>Storage:TestFallback:AllowFilesystem=true</c> and Azurite is unreachable,
/// <see cref="ObjectStorageRegistration"/> rewrites the provider to
/// LocalFilesystem and emits a startup warning. Without the opt-in the
/// rewrite must NOT happen — we keep production envs strict.
/// </summary>
[TestFixture]
public class TestFallbackTests
{
    private static IConfigurationRoot BuildConfig(IDictionary<string, string?> values)
        => new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    [Test]
    public void When_flag_enabled_and_azurite_unreachable_provider_is_rewritten_to_LocalFilesystem()
    {
        // Point at a port we know is closed (high-numbered, ephemeral).
        // The TCP probe in IsBlobEndpointReachable will fail within ~2s.
        var blockedPort = AllocateBlockedPort();
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["Storage:Provider"] = "Azurite",
            ["Storage:ConnectionString"] =
                "DefaultEndpointsProtocol=http;" +
                "AccountName=devstoreaccount1;" +
                "AccountKey=Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==;" +
                $"BlobEndpoint=http://127.0.0.1:{blockedPort}/devstoreaccount1;",
            ["Storage:TestFallback:AllowFilesystem"] = "true",
        });

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddObjectStorage(config);

        // Provider is rewritten in the configuration root.
        Assert.That(config["Storage:Provider"], Is.EqualTo("LocalFilesystem"));

        var provider = services.BuildServiceProvider();
        var storage = provider.GetRequiredService<IObjectStorage>();
        Assert.That(storage, Is.InstanceOf<LocalFilesystemObjectStorage>());
    }

    [Test]
    public void When_flag_disabled_provider_is_left_alone_even_when_unreachable()
    {
        var blockedPort = AllocateBlockedPort();
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["Storage:Provider"] = "Azurite",
            ["Storage:ConnectionString"] =
                "DefaultEndpointsProtocol=http;" +
                "AccountName=devstoreaccount1;" +
                "AccountKey=Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==;" +
                $"BlobEndpoint=http://127.0.0.1:{blockedPort}/devstoreaccount1;",
            // Flag explicitly absent.
        });

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddObjectStorage(config);

        Assert.That(config["Storage:Provider"], Is.EqualTo("Azurite"),
            "Without the test-fallback flag the registration must not rewrite the provider.");

        var provider = services.BuildServiceProvider();
        var storage = provider.GetRequiredService<IObjectStorage>();
        Assert.That(storage, Is.InstanceOf<AzureBlobObjectStorage>());
    }

    /// <summary>
    /// Returns a port that nothing is listening on. Allocates a TCP listener
    /// to obtain an ephemeral port from the OS, then closes it; the port is
    /// almost certainly free immediately afterwards.
    /// </summary>
    private static int AllocateBlockedPort()
    {
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
