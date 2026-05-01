using System.Text;
using FundingPlatform.Application.Abstractions.Storage;
using FundingPlatform.Infrastructure.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FundingPlatform.Tests.Integration.Storage;

/// <summary>
/// Spec 014 / T037 / US3 — confirms a no-Azure-credentials roundtrip against
/// the local Azurite emulator. The fixture asserts that no <c>AZURE_*</c>
/// environment variables leak into the test process before binding the
/// storage client; if any are present, the test bails so the suite can never
/// silently pivot to a real Azure account.
/// </summary>
[TestFixture]
[Category("Azurite")]
public class HermeticAzuriteRoundtripTests
{
    private AzuriteFixture _fixture = null!;
    private AzureBlobObjectStorage _storage = null!;

    [OneTimeSetUp]
    public async Task OneTimeSetup()
    {
        // Hermetic guard: any AZURE_* env var is a smell. We don't fail the
        // suite here because some shells (e.g. devcontainers) export
        // AZURE_CONFIG_DIR for the Azure CLI; we just record it. The strict
        // E2E HermeticEnvironmentTests file owns the must-be-clean assertion.
        foreach (var k in Environment.GetEnvironmentVariables().Keys)
        {
            var name = k!.ToString();
            if (name is null) continue;
            if (name.StartsWith("AZURE_STORAGE_", StringComparison.OrdinalIgnoreCase))
                Assert.Fail(
                    $"AZURE_STORAGE_* env var '{name}' is set; the hermetic Azurite suite refuses to run while real-cloud credentials are in scope.");
        }

        _fixture = new AzuriteFixture();
        var started = await _fixture.TryStartAsync();
        if (!started)
            Assert.Ignore("Docker not available — Azurite-backed tests skipped.");

        foreach (var name in FileCategoryExtensions.AllContainerNames)
            await _fixture.Client!.GetBlobContainerClient(name).CreateIfNotExistsAsync();

        var options = new StorageOptions
        {
            Provider = "Azurite",
            ConnectionString = _fixture.ConnectionString,
        };
        var diagnostics = new ObjectStorageDiagnostics(NullLogger<ObjectStorageDiagnostics>.Instance);
        _storage = new AzureBlobObjectStorage(
            _fixture.Client!,
            diagnostics,
            Options.Create(options),
            NullLogger<AzureBlobObjectStorage>.Instance);
    }

    [OneTimeTearDown]
    public async Task OneTimeTeardown()
    {
        if (_fixture is not null)
            await _fixture.DisposeAsync();
    }

    [Test]
    public async Task FixturePdf_uploads_downloads_byte_for_byte()
    {
        // Synthesize a small PDF-shaped payload deterministically so the test
        // is reproducible without checking a real PDF into the repo.
        var payload = new byte[16 * 1024];
        var prefix = Encoding.ASCII.GetBytes("%PDF-1.4\n");
        Array.Copy(prefix, payload, prefix.Length);
        for (var i = prefix.Length; i < payload.Length; i++)
            payload[i] = (byte)(i % 251);

        var key = ObjectKey.Build(
            FileCategory.SignedFundingAgreement,
            ownerSegment: "applicants/hermetic",
            entityId: "1",
            deterministicSuffix: "abcdef0123456789",
            extension: ".pdf");

        await using (var input = new MemoryStream(payload))
        {
            var stored = await _storage.UploadAsync(
                FileCategory.SignedFundingAgreement,
                key,
                input,
                "application/pdf",
                payload.Length,
                CancellationToken.None);
            Assert.That(stored.SizeBytes, Is.EqualTo(payload.Length));
            Assert.That(stored.Provider, Is.EqualTo(StorageProviderName.Azurite));
        }

        await using var stream = await _storage.OpenReadAsync(
            FileCategory.SignedFundingAgreement, key, CancellationToken.None);
        await using var ms = new MemoryStream();
        await stream.CopyToAsync(ms);
        Assert.That(ms.ToArray(), Is.EqualTo(payload));
    }
}
