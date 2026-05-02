using FundingPlatform.Application.Abstractions.Storage;
using FundingPlatform.Infrastructure.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FundingPlatform.Tests.Integration.Storage;

/// <summary>
/// T027 — verify the four FR-013 containers exist after the hosted service
/// runs against an Azurite endpoint (FR-016 / FR-027 — created private).
/// </summary>
[TestFixture]
[Category("Azurite")]
public class ContainerBootstrapTests
{
    private AzuriteFixture _fixture = null!;

    [OneTimeSetUp]
    public async Task OneTimeSetup()
    {
        _fixture = new AzuriteFixture();
        if (!await _fixture.TryStartAsync())
            Assert.Ignore("Docker not available — Azurite-backed tests skipped.");
    }

    [OneTimeTearDown]
    public async Task OneTimeTeardown()
    {
        if (_fixture is not null)
            await _fixture.DisposeAsync();
    }

    [Test]
    public async Task EnsureContainersHostedService_creates_all_four_containers()
    {
        var options = new StorageOptions
        {
            Provider = "Azurite",
            ConnectionString = _fixture.ConnectionString,
        };

        var hosted = new EnsureContainersHostedService(
            _fixture.Client!,
            Options.Create(options),
            NullLogger<EnsureContainersHostedService>.Instance);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        await hosted.StartAsync(CancellationToken.None);
        sw.Stop();

        Assert.That(sw.Elapsed, Is.LessThan(TimeSpan.FromSeconds(30)),
            "Container bootstrap exceeded the 30 s budget (SC-008).");

        foreach (var name in FileCategoryExtensions.AllContainerNames)
        {
            var container = _fixture.Client!.GetBlobContainerClient(name);
            // ExistsAsync returns Response<bool>; unwrap .Value for the assertion
            // so NUnit doesn't compare the response wrapper to a primitive.
            Assert.That((await container.ExistsAsync()).Value, Is.True, $"Container '{name}' missing.");
        }
    }

    [Test]
    public async Task EnsureContainersHostedService_is_idempotent()
    {
        var options = new StorageOptions
        {
            Provider = "Azurite",
            ConnectionString = _fixture.ConnectionString,
        };

        var hosted = new EnsureContainersHostedService(
            _fixture.Client!,
            Options.Create(options),
            NullLogger<EnsureContainersHostedService>.Instance);

        await hosted.StartAsync(CancellationToken.None);
        // Second run must not throw.
        await hosted.StartAsync(CancellationToken.None);

        foreach (var name in FileCategoryExtensions.AllContainerNames)
            Assert.That((await _fixture.Client!.GetBlobContainerClient(name).ExistsAsync()).Value, Is.True);
    }
}
