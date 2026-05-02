using System.Text;
using FundingPlatform.Application.Abstractions.Storage;
using FundingPlatform.Infrastructure.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FundingPlatform.Tests.Integration.Storage;

/// <summary>
/// T026 — LocalFilesystemObjectStorage parity tests. Real disk IO; covers
/// upload/download/exists/delete + URL-not-supported error.
/// </summary>
[TestFixture]
public class LocalFilesystemObjectStorageTests
{
    private string _tempRoot = string.Empty;
    private LocalFilesystemObjectStorage _storage = null!;

    [SetUp]
    public void Setup()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "ps-014-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);

        var options = new StorageOptions
        {
            Provider = "LocalFilesystem",
            LocalFilesystem = new StorageLocalFilesystemOptions { RootPath = _tempRoot },
        };
        var diagnostics = new ObjectStorageDiagnostics(NullLogger<ObjectStorageDiagnostics>.Instance);
        _storage = new LocalFilesystemObjectStorage(
            diagnostics,
            Options.Create(options),
            NullLogger<LocalFilesystemObjectStorage>.Instance);
    }

    [TearDown]
    public void Teardown()
    {
        if (Directory.Exists(_tempRoot))
        {
            try { Directory.Delete(_tempRoot, recursive: true); } catch { /* best-effort */ }
        }
    }

    private static ObjectKey BuildKey(string suffix = "test1234abcd5678") =>
        ObjectKey.Build(
            FileCategory.SignedFundingAgreement,
            "applicants/abc",
            "entity-123",
            suffix,
            ".pdf");

    [Test]
    public async Task Upload_then_download_roundtrips_bytes()
    {
        var payload = Encoding.UTF8.GetBytes("hello, world — funding-platform 014");
        var key = BuildKey();

        await using (var input = new MemoryStream(payload))
        {
            var stored = await _storage.UploadAsync(
                FileCategory.SignedFundingAgreement, key, input,
                "application/pdf", payload.Length, CancellationToken.None);

            Assert.That(stored.SizeBytes, Is.EqualTo(payload.Length));
            Assert.That(stored.Provider, Is.EqualTo(StorageProviderName.LocalFilesystem));
            Assert.That(stored.Container, Is.EqualTo("signed-funding-agreements"));
        }

        await using (var read = await _storage.OpenReadAsync(
            FileCategory.SignedFundingAgreement, key, CancellationToken.None))
        await using (var ms = new MemoryStream())
        {
            await read.CopyToAsync(ms);
            Assert.That(ms.ToArray(), Is.EqualTo(payload));
        }
    }

    [Test]
    public async Task Exists_reflects_state()
    {
        var key = BuildKey("abcdef0123456789");
        Assert.That(
            await _storage.ExistsAsync(FileCategory.SignedFundingAgreement, key, CancellationToken.None),
            Is.False);

        await using (var input = new MemoryStream(new byte[] { 1, 2, 3 }))
        {
            await _storage.UploadAsync(
                FileCategory.SignedFundingAgreement, key, input,
                "application/pdf", 3, CancellationToken.None);
        }

        Assert.That(
            await _storage.ExistsAsync(FileCategory.SignedFundingAgreement, key, CancellationToken.None),
            Is.True);
    }

    [Test]
    public async Task Delete_is_idempotent()
    {
        var key = BuildKey("0123456789abcdef");

        // Delete absent — should not throw.
        await _storage.DeleteAsync(FileCategory.SignedFundingAgreement, key, CancellationToken.None);

        await using (var input = new MemoryStream(new byte[] { 9, 8, 7 }))
        {
            await _storage.UploadAsync(
                FileCategory.SignedFundingAgreement, key, input,
                "application/pdf", 3, CancellationToken.None);
        }

        await _storage.DeleteAsync(FileCategory.SignedFundingAgreement, key, CancellationToken.None);
        Assert.That(
            await _storage.ExistsAsync(FileCategory.SignedFundingAgreement, key, CancellationToken.None),
            Is.False);

        // Delete absent again — still no throw.
        await _storage.DeleteAsync(FileCategory.SignedFundingAgreement, key, CancellationToken.None);
    }

    [Test]
    public void ResolveServingHandle_rejects_TimeLimitedUrl()
    {
        var key = BuildKey("1111aaaa2222bbbb");

        Assert.ThrowsAsync<LocalProviderUrlNotSupportedException>(async () =>
            await _storage.ResolveServingHandleAsync(
                FileCategory.SignedFundingAgreement,
                key,
                ServingMode.TimeLimitedUrl,
                CancellationToken.None));
    }

    [Test]
    public async Task ResolveServingHandle_BackendStream_returns_payload()
    {
        var payload = Encoding.UTF8.GetBytes("backend-stream payload");
        var key = BuildKey("ffeeddccbbaa9988");

        await using (var input = new MemoryStream(payload))
        {
            await _storage.UploadAsync(
                FileCategory.SignedFundingAgreement, key, input,
                "application/pdf", payload.Length, CancellationToken.None);
        }

        var handle = await _storage.ResolveServingHandleAsync(
            FileCategory.SignedFundingAgreement,
            key,
            ServingMode.BackendStream,
            CancellationToken.None);

        Assert.That(handle, Is.InstanceOf<BackendStreamHandle>());
        var stream = ((BackendStreamHandle)handle).Content;
        await using (stream)
        await using (var ms = new MemoryStream())
        {
            await stream.CopyToAsync(ms);
            Assert.That(ms.ToArray(), Is.EqualTo(payload));
        }
    }

    [Test]
    public void OpenRead_throws_when_blob_missing()
    {
        var key = BuildKey("doesnotexisthere");
        Assert.ThrowsAsync<ObjectNotFoundException>(async () =>
            await _storage.OpenReadAsync(FileCategory.SignedFundingAgreement, key, CancellationToken.None));
    }
}
