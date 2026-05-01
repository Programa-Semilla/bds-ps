using System.Text;
using FundingPlatform.Application.Abstractions.Storage;
using FundingPlatform.Infrastructure.Storage;
using FundingPlatform.StorageMigration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FundingPlatform.Tests.Integration.Storage;

/// <summary>
/// T045 — running the migration twice MUST report Skipped-Existing for every
/// entry on the second run and exit 0 (FR-024 acceptance scenario 2).
/// </summary>
[TestFixture]
[Category("Azurite")]
public class MigrationIdempotencyTests
{
    private AzuriteFixture _fixture = null!;
    private AzureBlobObjectStorage _storage = null!;
    private string _legacyRoot = null!;

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
        _legacyRoot = Directory.CreateTempSubdirectory("ps-migration-idem-").FullName;
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
    public async Task Second_run_reports_skipped_existing_for_every_entry()
    {
        var fileA = Path.Combine(_legacyRoot, "a.pdf");
        var fileB = Path.Combine(_legacyRoot, "b.pdf");
        await File.WriteAllBytesAsync(fileA, Encoding.UTF8.GetBytes("aaa"));
        await File.WriteAllBytesAsync(fileB, Encoding.UTF8.GetBytes("bbb"));

        var resolver = new LegacyRowResolver();
        resolver.AddManually(fileA, FileCategory.SignedFundingAgreement, "applicants/u1", "ent-1");
        resolver.AddManually(fileB, FileCategory.SignedFundingAgreement, "applicants/u1", "ent-2");

        var runner = new MigrationRunner(_storage, resolver, NullLogger.Instance);

        // Manifests must live OUTSIDE the legacy root so they aren't picked up by
        // the second walk as additional "files to migrate".
        var manifestDir = Directory.CreateTempSubdirectory("ps-migration-manifests-").FullName;
        var manifest1 = Path.Combine(manifestDir, "run1.jsonl");
        var summary1 = await runner.RunAsync(_legacyRoot, manifest1, parallelism: 1, CancellationToken.None);
        Assert.That(summary1.Uploaded, Is.EqualTo(2));
        Assert.That(summary1.Failed, Is.EqualTo(0));

        var manifest2 = Path.Combine(manifestDir, "run2.jsonl");
        var summary2 = await runner.RunAsync(_legacyRoot, manifest2, parallelism: 1, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(summary2.Total, Is.EqualTo(2));
            Assert.That(summary2.Uploaded, Is.EqualTo(0));
            Assert.That(summary2.Skipped, Is.EqualTo(2));
            Assert.That(summary2.Failed, Is.EqualTo(0));
        });

        // Exit code parity: failed=0 means CLI returns 0.
        var entries = new List<MigrationManifestEntry>();
        await foreach (var e in MigrationManifest.ReadAsync(manifest2))
            entries.Add(e);
        Assert.That(entries.All(e => e.Outcome == MigrationManifest.OutcomeNames.SkippedExisting), Is.True);
    }
}
