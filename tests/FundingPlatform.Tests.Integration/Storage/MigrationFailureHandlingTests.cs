using System.Runtime.InteropServices;
using System.Text;
using FundingPlatform.Application.Abstractions.Storage;
using FundingPlatform.Infrastructure.Storage;
using FundingPlatform.StorageMigration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FundingPlatform.Tests.Integration.Storage;

/// <summary>
/// T046 — when one file is unreadable, the manifest must record Failed for
/// that file, the run exits non-zero, AND every other file still uploads
/// (no fail-fast on a single bad entry, per FR-024 acceptance scenario 4).
/// </summary>
[TestFixture]
[Category("Azurite")]
public class MigrationFailureHandlingTests
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
        _legacyRoot = Directory.CreateTempSubdirectory("ps-migration-fail-").FullName;
    }

    [TearDown]
    public void Teardown()
    {
        // Restore any chmod-000 file before deletion, otherwise rmdir fails.
        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            foreach (var f in Directory.EnumerateFiles(_legacyRoot, "*", SearchOption.AllDirectories))
            {
                try { File.SetUnixFileMode(f, UnixFileMode.UserRead | UnixFileMode.UserWrite); } catch { }
            }
        }
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
    public async Task Unreadable_file_reports_failed_other_files_still_upload()
    {
        // chmod 000 only blocks reads when the test process isn't root.
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux) &&
            !RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            Assert.Ignore("chmod-based unreadable-file trick requires POSIX.");

        if (Environment.UserName == "root" || (geteuid_safe() == 0))
            Assert.Ignore("Running as root bypasses file permissions.");

        var ok = Path.Combine(_legacyRoot, "ok.pdf");
        var bad = Path.Combine(_legacyRoot, "bad.pdf");
        var ok2 = Path.Combine(_legacyRoot, "ok2.pdf");

        await File.WriteAllBytesAsync(ok, Encoding.UTF8.GetBytes("good-1"));
        await File.WriteAllBytesAsync(bad, Encoding.UTF8.GetBytes("locked"));
        await File.WriteAllBytesAsync(ok2, Encoding.UTF8.GetBytes("good-2"));

        // Strip read permission. UnixFileMode.None = 000. Guarded by the
        // POSIX runtime check above; suppressed because the analyzer can't
        // see through Assert.Ignore.
#pragma warning disable CA1416
        File.SetUnixFileMode(bad, UnixFileMode.None);
#pragma warning restore CA1416

        var resolver = new LegacyRowResolver();
        resolver.AddManually(ok, FileCategory.SignedFundingAgreement, "applicants/u1", "ok-1");
        resolver.AddManually(bad, FileCategory.SignedFundingAgreement, "applicants/u1", "bad-1");
        resolver.AddManually(ok2, FileCategory.SignedFundingAgreement, "applicants/u1", "ok-2");

        var manifest = Path.Combine(_legacyRoot, "manifest.jsonl");
        var runner = new MigrationRunner(_storage, resolver, NullLogger.Instance);
        var summary = await runner.RunAsync(_legacyRoot, manifest, parallelism: 1, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(summary.Total, Is.EqualTo(3));
            Assert.That(summary.Uploaded, Is.EqualTo(2), "Healthy files must still upload.");
            Assert.That(summary.Failed, Is.EqualTo(1), "Unreadable file must be recorded as Failed.");
        });

        // Process-level exit code maps non-zero when failed > 0; verify by inspecting the manifest.
        var entries = new List<MigrationManifestEntry>();
        await foreach (var e in MigrationManifest.ReadAsync(manifest))
            entries.Add(e);

        var failedEntry = entries.Single(e => e.Outcome == MigrationManifest.OutcomeNames.Failed);
        Assert.That(failedEntry.LegacyPath, Is.EqualTo(bad));
        Assert.That(failedEntry.Error, Is.Not.Null.And.Not.Empty);

        // Confirm the two healthy files made it to the backend.
        foreach (var legacy in new[] { ok, ok2 })
        {
            var entry = entries.Single(e => e.LegacyPath == legacy);
            Assert.That(entry.Outcome, Is.EqualTo(MigrationManifest.OutcomeNames.Uploaded));
            var key = ObjectKey.Parse(entry.ComputedKey);
            Assert.That(await _storage.ExistsAsync(FileCategory.SignedFundingAgreement, key, CancellationToken.None), Is.True);
        }
    }

    // P/Invoke into libc geteuid so we can short-circuit the test under root without
    // pulling in Mono.Posix. Returns 0 when not POSIX.
    [DllImport("libc", EntryPoint = "geteuid", SetLastError = true)]
    private static extern uint geteuid();

    private static uint geteuid_safe()
    {
        try { return geteuid(); }
        catch { return uint.MaxValue; }
    }
}
