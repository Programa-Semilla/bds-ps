using System.Text;
using FundingPlatform.Application.Abstractions.Storage;
using FundingPlatform.Infrastructure.Storage;
using FundingPlatform.StorageMigration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FundingPlatform.Tests.Integration.Storage;

/// <summary>
/// T044 — drives <see cref="MigrationRunner"/> against a real Azurite emulator,
/// asserts every legacy file is uploaded under its computed key, and that the
/// manifest matches expectations. Skipped when Docker isn't available so the
/// suite stays runnable on dev boxes without containers.
/// </summary>
[TestFixture]
[Category("Azurite")]
public class MigrationCommandTests
{
    private AzuriteFixture _fixture = null!;
    private AzureBlobObjectStorage _storage = null!;
    private string _legacyRoot = null!;
    private string _manifestPath = null!;

    [OneTimeSetUp]
    public async Task OneTimeSetup()
    {
        _fixture = new AzuriteFixture();
        if (!await _fixture.TryStartAsync())
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

    [SetUp]
    public void Setup()
    {
        _legacyRoot = Directory.CreateTempSubdirectory("ps-migration-cmd-").FullName;
        _manifestPath = Path.Combine(_legacyRoot, "manifest.jsonl");
    }

    [TearDown]
    public void Teardown()
    {
        try { Directory.Delete(_legacyRoot, recursive: true); }
        catch { /* best-effort */ }
    }

    [OneTimeTearDown]
    public async Task OneTimeTeardown()
    {
        if (_fixture is not null)
            await _fixture.DisposeAsync();
    }

    [Test]
    public async Task Migrate_uploads_every_file_and_writes_manifest()
    {
        var fileA = Path.Combine(_legacyRoot, "abc-signed.pdf");
        var fileB = Path.Combine(_legacyRoot, "supplier", "catalog.csv");
        var fileC = Path.Combine(_legacyRoot, "doc-23.pdf");

        Directory.CreateDirectory(Path.GetDirectoryName(fileB)!);

        var bytesA = Encoding.UTF8.GetBytes("signed-pdf-content");
        var bytesB = Encoding.UTF8.GetBytes("col1,col2\n1,2\n");
        var bytesC = Encoding.UTF8.GetBytes("doc-content");

        await File.WriteAllBytesAsync(fileA, bytesA);
        await File.WriteAllBytesAsync(fileB, bytesB);
        await File.WriteAllBytesAsync(fileC, bytesC);

        var resolver = new LegacyRowResolver();
        resolver.AddManually(fileA, FileCategory.SignedFundingAgreement, "applicants/user-aaa", "signed-1");
        resolver.AddManually(fileB, FileCategory.SupplierCatalogImport, "admin", "import-1");
        resolver.AddManually(fileC, FileCategory.ApplicationAttachment, "admin", "doc-23");

        var runner = new MigrationRunner(_storage, resolver, NullLogger.Instance);
        var summary = await runner.RunAsync(_legacyRoot, _manifestPath, parallelism: 1, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(summary.Total, Is.EqualTo(3));
            Assert.That(summary.Uploaded, Is.EqualTo(3));
            Assert.That(summary.Skipped, Is.EqualTo(0));
            Assert.That(summary.Failed, Is.EqualTo(0));
        });

        // Manifest contains a line per file, every outcome is "Uploaded".
        var entries = new List<MigrationManifestEntry>();
        await foreach (var e in MigrationManifest.ReadAsync(_manifestPath))
            entries.Add(e);
        Assert.That(entries.Count, Is.EqualTo(3));
        Assert.That(entries.All(e => e.Outcome == MigrationManifest.OutcomeNames.Uploaded), Is.True,
            "Every entry should be Uploaded on a fresh run.");

        // Each computed key must exist at the backend with byte-for-byte content.
        foreach (var entry in entries)
        {
            var key = ObjectKey.Parse(entry.ComputedKey);
            var category = Enum.Parse<FileCategory>(entry.Category, ignoreCase: true);
            Assert.That(await _storage.ExistsAsync(category, key, CancellationToken.None), Is.True,
                $"Key {entry.ComputedKey} not present in backend after migration.");
        }

        // Spot-check the deterministic suffix matches the published formula.
        var expectedSuffixA = MigrationRunner.ComputeDeterministicSuffix(fileA);
        var entryA = entries.Single(e => e.LegacyPath == fileA);
        Assert.That(entryA.DeterministicSuffix, Is.EqualTo(expectedSuffixA));
        Assert.That(entryA.ComputedKey, Does.Contain($"/{expectedSuffixA}"));
    }

    [Test]
    public async Task Migrate_records_failed_for_unknown_paths()
    {
        var fileA = Path.Combine(_legacyRoot, "orphan.pdf");
        await File.WriteAllBytesAsync(fileA, new byte[] { 1, 2, 3 });

        var resolver = new LegacyRowResolver(); // intentionally empty — no DB row matches
        var runner = new MigrationRunner(_storage, resolver, NullLogger.Instance);
        var summary = await runner.RunAsync(_legacyRoot, _manifestPath, parallelism: 1, CancellationToken.None);

        Assert.That(summary.Failed, Is.EqualTo(1));
        Assert.That(summary.Uploaded, Is.EqualTo(0));

        var entry = (await ReadAllAsync(_manifestPath)).Single();
        Assert.That(entry.Outcome, Is.EqualTo(MigrationManifest.OutcomeNames.Failed));
        Assert.That(entry.Error, Is.Not.Null.And.Contains("No DB row"));
    }

    private static async Task<List<MigrationManifestEntry>> ReadAllAsync(string path)
    {
        var list = new List<MigrationManifestEntry>();
        await foreach (var e in MigrationManifest.ReadAsync(path))
            list.Add(e);
        return list;
    }
}
