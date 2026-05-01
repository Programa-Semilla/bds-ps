using System.Text;
using FundingPlatform.Application.Abstractions.Storage;
using FundingPlatform.Infrastructure.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FundingPlatform.Tests.Integration.Storage;

/// <summary>
/// T025 — Azurite roundtrip. Skipped when Docker isn't available so the
/// suite stays runnable in non-Docker environments. Production CI must have
/// Docker (Aspire already requires it for the SQL Server container).
/// </summary>
[TestFixture]
[Category("Azurite")]
public class AzuriteObjectStorageTests
{
    private AzuriteFixture _fixture = null!;
    private AzureBlobObjectStorage _storage = null!;

    [OneTimeSetUp]
    public async Task OneTimeSetup()
    {
        _fixture = new AzuriteFixture();
        var started = await _fixture.TryStartAsync();
        if (!started)
        {
            Assert.Ignore("Docker not available — Azurite-backed tests skipped.");
        }

        // Pre-create the four well-known containers (FR-013).
        foreach (var name in FileCategoryExtensions.AllContainerNames)
        {
            await _fixture.Client!.GetBlobContainerClient(name).CreateIfNotExistsAsync();
        }

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

    private static ObjectKey BuildKey(string suffix = "abcdef0123456789") =>
        ObjectKey.Build(
            FileCategory.SignedFundingAgreement,
            "applicants/abc",
            "entity-1",
            suffix,
            ".pdf");

    [Test]
    public async Task Upload_then_download_byte_for_byte_match()
    {
        var payload = Encoding.UTF8.GetBytes("azurite-roundtrip");
        var key = BuildKey("aaaa1111bbbb2222");

        await using (var input = new MemoryStream(payload))
        {
            var stored = await _storage.UploadAsync(
                FileCategory.SignedFundingAgreement,
                key,
                input,
                "application/pdf",
                payload.Length,
                CancellationToken.None);
            Assert.That(stored.Provider, Is.EqualTo(StorageProviderName.Azurite));
            Assert.That(stored.SizeBytes, Is.EqualTo(payload.Length));
        }

        await using var stream = await _storage.OpenReadAsync(
            FileCategory.SignedFundingAgreement, key, CancellationToken.None);
        await using var ms = new MemoryStream();
        await stream.CopyToAsync(ms);
        Assert.That(ms.ToArray(), Is.EqualTo(payload));
    }

    [Test]
    public async Task Exists_and_delete_round_trip()
    {
        var key = BuildKey("3333cccc4444dddd");

        Assert.That(await _storage.ExistsAsync(
            FileCategory.SignedFundingAgreement, key, CancellationToken.None), Is.False);

        await using (var input = new MemoryStream(new byte[] { 1, 2, 3 }))
        {
            await _storage.UploadAsync(
                FileCategory.SignedFundingAgreement,
                key,
                input,
                "application/pdf",
                3,
                CancellationToken.None);
        }

        Assert.That(await _storage.ExistsAsync(
            FileCategory.SignedFundingAgreement, key, CancellationToken.None), Is.True);

        await _storage.DeleteAsync(FileCategory.SignedFundingAgreement, key, CancellationToken.None);

        Assert.That(await _storage.ExistsAsync(
            FileCategory.SignedFundingAgreement, key, CancellationToken.None), Is.False);
    }

    [Test]
    public void OpenRead_missing_throws_ObjectNotFoundException()
    {
        var key = BuildKey("missingkeyaaaaaa");
        Assert.ThrowsAsync<ObjectNotFoundException>(async () =>
            await _storage.OpenReadAsync(
                FileCategory.SignedFundingAgreement, key, CancellationToken.None));
    }
}
