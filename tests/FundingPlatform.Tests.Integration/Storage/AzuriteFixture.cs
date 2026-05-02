using System.Diagnostics;
using Azure.Storage.Blobs;

namespace FundingPlatform.Tests.Integration.Storage;

/// <summary>
/// Minimal Azurite fixture: spins up a fresh Azurite container per test class via
/// <c>docker run</c>. Tests using this fixture are skipped when Docker isn't
/// available so the suite stays runnable in environments that haven't installed
/// it. Production CI will have Docker (Aspire requires it).
/// </summary>
public sealed class AzuriteFixture : IAsyncDisposable
{
    public string? ConnectionString { get; private set; }
    public BlobServiceClient? Client { get; private set; }

    private string? _containerId;

    public async Task<bool> TryStartAsync()
    {
        if (!IsDockerAvailable())
            return false;

        // Bind to an OS-allocated ephemeral port so parallel test workers do
        // not race on a fixed range. Random.Next(0, 5000) was prone to
        // collisions when xUnit launched multiple AzuriteFixtures concurrently.
        var port = AllocateEphemeralPort();

        var name = $"ps-014-azurite-{Guid.NewGuid():N}".Substring(0, 24);
        var psi = new ProcessStartInfo("docker", string.Join(' ',
            "run", "-d", "--rm",
            "--name", name,
            "-p", $"{port}:10000",
            "mcr.microsoft.com/azure-storage/azurite:latest",
            "azurite-blob", "--blobHost", "0.0.0.0", "--skipApiVersionCheck"))
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        using var proc = Process.Start(psi)!;
        _containerId = (await proc.StandardOutput.ReadToEndAsync()).Trim();
        var err = await proc.StandardError.ReadToEndAsync();
        await proc.WaitForExitAsync();

        if (proc.ExitCode != 0)
            throw new InvalidOperationException($"docker run failed: {err}");

        ConnectionString =
            "DefaultEndpointsProtocol=http;" +
            "AccountName=devstoreaccount1;" +
            "AccountKey=Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==;" +
            $"BlobEndpoint=http://127.0.0.1:{port}/devstoreaccount1;";

        Client = new BlobServiceClient(ConnectionString);

        // Wait for Azurite to respond.
        for (var i = 0; i < 40; i++)
        {
            try
            {
                await Client.GetPropertiesAsync();
                return true;
            }
            catch
            {
                await Task.Delay(500);
            }
        }
        throw new TimeoutException("Azurite did not become healthy within 20 seconds.");
    }

    public async ValueTask DisposeAsync()
    {
        if (string.IsNullOrEmpty(_containerId))
            return;

        var psi = new ProcessStartInfo("docker", $"rm -f {_containerId}")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        using var proc = Process.Start(psi);
        if (proc is not null)
        {
            await proc.WaitForExitAsync();
        }
    }

    private static int AllocateEphemeralPort()
    {
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            return ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }

    private static bool IsDockerAvailable()
    {
        try
        {
            var psi = new ProcessStartInfo("docker", "info")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            using var proc = Process.Start(psi);
            if (proc is null) return false;
            proc.WaitForExit(2000);
            return proc.HasExited && proc.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}
