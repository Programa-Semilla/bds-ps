using System.Diagnostics;
using Aspire.Hosting;
using Aspire.Hosting.Testing;
using Azure.Storage.Blobs;

namespace FundingPlatform.Tests.E2E.Fixtures;

public class AspireFixture : IAsyncDisposable
{
    /// <summary>
    /// Spec 014 / FR-013 — the four canonical container names. Mirrored here as a
    /// constant array so the fixture doesn't take a project reference on
    /// FundingPlatform.Application just for an enum.
    /// </summary>
    public static readonly string[] CanonicalContainerNames =
    [
        "signed-funding-agreements",
        "supplier-catalog-imports",
        "application-attachments",
        "generated-artifacts",
    ];

    private DistributedApplication? _app;
    public string BaseUrl { get; private set; } = string.Empty;
    public string ConnectionString { get; private set; } = string.Empty;

    /// <summary>Spec 014 / T035 — Azurite blob endpoint connection string.</summary>
    public string? BlobsConnectionString { get; private set; }

    /// <summary>
    /// Spec 014 / T036 — when Azurite cannot start within timeout AND
    /// <c>Storage:TestFallback:AllowFilesystem</c> is enabled, the fixture
    /// surfaces this flag so individual tests can document the degraded mode.
    /// </summary>
    public bool FellBackToFilesystem { get; private set; }

    public async Task StartAsync()
    {
        // --EphemeralStorage=true tells the AppHost to skip both the persistent
        // SQL data volume AND the AddSqlProject auto-deploy. Tests own the schema
        // deployment themselves via DeployDacpacAsync below.
        var builder = await DistributedApplicationTestingBuilder
            .CreateAsync<Projects.FundingPlatform_AppHost>(["--EphemeralStorage=true"]);

        _app = await builder.BuildAsync();
        await _app.StartAsync();

        await DeployDacpacAsync();

        ConnectionString = await _app.GetConnectionStringAsync("fundingdb") ?? string.Empty;

        // Spec 014 / T035 — wait for the Azurite-backed `blobs` resource to be
        // reachable before yielding to the test. Aspire spins it up async; if we
        // hand control to the suite before the emulator is responding, the first
        // upload faces a 60s SDK retry storm and the test times out for the
        // wrong reason. Falls through with FellBackToFilesystem=true when the
        // operator opts into the filesystem fallback (FR-008).
        await EnsureBlobsResourceReadyAsync();

        // Use http — the test environment may not trust the dev HTTPS certificate
        var webapp = _app.GetEndpoint("webapp", "http");
        BaseUrl = webapp.ToString().TrimEnd('/');

        // Verify the web app is actually responding
        await WaitForWebAppAsync();
    }

    /// <summary>
    /// Spec 014 / T035 — Wait for the Azurite Aspire resource to be healthy and
    /// pre-create the four containers (idempotent). The Web app's
    /// <c>EnsureContainersHostedService</c> does the same, but tests need an
    /// explicit guarantee before running.
    /// </summary>
    private async Task EnsureBlobsResourceReadyAsync()
    {
        if (_app is null) return;

        BlobsConnectionString = await _app.GetConnectionStringAsync("blobs");
        if (string.IsNullOrEmpty(BlobsConnectionString))
        {
            // No blobs resource at all (provider may be LocalFilesystem).
            // Honour the FR-008 fallback flag if the operator opted in.
            var allowFallback = string.Equals(
                Environment.GetEnvironmentVariable("Storage__TestFallback__AllowFilesystem"),
                "true",
                StringComparison.OrdinalIgnoreCase);
            FellBackToFilesystem = allowFallback;
            return;
        }

        var client = new BlobServiceClient(BlobsConnectionString);

        var deadline = DateTime.UtcNow.AddSeconds(30);
        Exception? lastException = null;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                await client.GetPropertiesAsync();
                lastException = null;
                break;
            }
            catch (Exception ex)
            {
                lastException = ex;
                await Task.Delay(500);
            }
        }

        if (lastException is not null)
        {
            var allowFallback = string.Equals(
                Environment.GetEnvironmentVariable("Storage__TestFallback__AllowFilesystem"),
                "true",
                StringComparison.OrdinalIgnoreCase);
            if (!allowFallback)
                throw new TimeoutException(
                    $"Azurite did not become healthy within 30s. " +
                    $"Set Storage__TestFallback__AllowFilesystem=true to enable the FR-008 filesystem fallback. " +
                    $"Last error: {lastException.Message}", lastException);
            FellBackToFilesystem = true;
            return;
        }

        // Pre-create the four canonical containers. The Web app does this on
        // startup too; we mirror it so a test that hits storage before the web
        // app's hosted service finishes still has containers to work with.
        foreach (var name in CanonicalContainerNames)
        {
            await client.GetBlobContainerClient(name).CreateIfNotExistsAsync();
        }
    }

    /// <summary>
    /// Spec 014 / T035 — helper exposing a configured BlobServiceClient so tests
    /// can seed and inspect blobs directly without re-deriving the connection
    /// string. Returns null when the fixture fell back to filesystem mode.
    /// </summary>
    public BlobServiceClient? CreateBlobServiceClient()
        => string.IsNullOrEmpty(BlobsConnectionString)
            ? null
            : new BlobServiceClient(BlobsConnectionString);

    /// <summary>
    /// Spec 014 / T035 — helper for tests to compute a deterministic ObjectKey-shaped
    /// path without taking a project reference on Application. Mirrors the
    /// canonical format `{container}/{owner-segment}/{entity-id}/{suffix}{ext}`.
    /// </summary>
    public static string ComputeKey(string container, string ownerSegment, string entityId, string suffix, string extension)
    {
        var ext = extension.StartsWith('.') ? extension : "." + extension;
        return $"{container}/{ownerSegment.Trim('/').ToLowerInvariant()}/{entityId.ToLowerInvariant()}/{suffix.ToLowerInvariant()}{ext.ToLowerInvariant()}";
    }

    private async Task WaitForWebAppAsync()
    {
        using var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true
        };
        using var client = new HttpClient(handler) { BaseAddress = new Uri(BaseUrl) };

        for (var i = 0; i < 30; i++)
        {
            try
            {
                var response = await client.GetAsync("/");
                if (response.IsSuccessStatusCode || response.StatusCode == System.Net.HttpStatusCode.Found)
                    return;
            }
            catch (HttpRequestException)
            {
                // Web app not ready yet
            }
            await Task.Delay(1000);
        }

        throw new TimeoutException($"Web app at {BaseUrl} did not become healthy within 30 seconds");
    }

    private async Task DeployDacpacAsync()
    {
        if (_app is null) throw new InvalidOperationException("App not started");

        var connectionString = await _app.GetConnectionStringAsync("fundingdb");

        if (string.IsNullOrEmpty(connectionString))
            throw new InvalidOperationException("Could not resolve 'fundingdb' connection string from Aspire host");

        var dacpacPath = FindDacpac();

        var sqlpackagePath = FindOnPath("sqlpackage")
            ?? throw new FileNotFoundException(
                "sqlpackage not found. Install it with: dotnet tool install -g microsoft.sqlpackage");

        var psi = new ProcessStartInfo
        {
            FileName = sqlpackagePath,
            Arguments = string.Join(" ",
                "/Action:Publish",
                $"/SourceFile:\"{dacpacPath}\"",
                $"/TargetConnectionString:\"{connectionString}\"",
                "/p:VerifyDeployment=false",
                "/p:BlockOnPossibleDataLoss=false",
                // The DefaultCurrency SqlCmdVariable is referenced by SeedData.sql to seed the
                // SystemConfigurations row; the dacpac embeds an empty value when the build-time
                // MSBuild property is unset, so the test fixture must override it explicitly to
                // avoid a blank Value that fails Required validation on admin Configuration save.
                "/v:DefaultCurrency=COP"),
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        // Ensure DOTNET_ROOT is set so sqlpackage can find the .NET runtime
        var dotnetRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dotnet");
        if (Directory.Exists(dotnetRoot))
        {
            psi.Environment["DOTNET_ROOT"] = dotnetRoot;
        }

        using var proc = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start sqlpackage");

        var stdout = await proc.StandardOutput.ReadToEndAsync();
        var stderr = await proc.StandardError.ReadToEndAsync();
        await proc.WaitForExitAsync();

        if (proc.ExitCode != 0)
            throw new InvalidOperationException(
                $"sqlpackage failed (exit {proc.ExitCode}):\n{stderr}\n{stdout}");
    }

    private static string FindDacpac()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "FundingPlatform.slnx")))
            dir = dir.Parent;

        if (dir is null)
            throw new FileNotFoundException("Could not find solution root (FundingPlatform.slnx)");

        var dacpac = Path.Combine(dir.FullName,
            "src", "FundingPlatform.Database", "bin", "Debug", "FundingPlatform.Database.dacpac");

        if (!File.Exists(dacpac))
            throw new FileNotFoundException($"Dacpac not found at {dacpac}. Run 'dotnet build src/FundingPlatform.Database' first.");

        return dacpac;
    }

    private static string? FindOnPath(string executable)
    {
        // Check well-known .NET tools directory first
        var dotnetToolsDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dotnet", "tools");
        var dotnetToolPath = Path.Combine(dotnetToolsDir, executable);
        if (File.Exists(dotnetToolPath))
            return dotnetToolPath;

        // Fall back to PATH search
        var pathVar = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in pathVar.Split(Path.PathSeparator))
        {
            var candidate = Path.Combine(dir, executable);
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }

    public async ValueTask DisposeAsync()
    {
        if (_app is not null)
        {
            await _app.DisposeAsync();
        }
    }
}
