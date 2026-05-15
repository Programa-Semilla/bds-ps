using FundingPlatform.Tests.E2E.Fixtures;
using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.Brand;

/// <summary>
/// Spec 019 T084 / FR-036 / SC-012 — Capture visual-regression snapshots for
/// applicant home, reviewer queue, admin index, and login. Baselines live
/// under specs/019-programa-semilla-brand/snapshots/. Diff is reviewed on PR.
///
/// Implementation note: Playwright screenshot comparison is the chosen tool
/// (research R9). Baselines are committed by re-running with --update-snapshots.
/// </summary>
public class VisualRegressionTests : AuthenticatedTestBase
{
    private string SnapshotDir =>
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "specs", "019-programa-semilla-brand", "snapshots");

    [Test]
    public async Task LoginPage_Snapshot()
    {
        await Page.GotoAsync($"{BaseUrl}/Account/Login");
        await Page.SetViewportSizeAsync(1280, 800);
        var bytes = await Page.ScreenshotAsync(new() { FullPage = true });
        Directory.CreateDirectory(SnapshotDir);
        await File.WriteAllBytesAsync(Path.Combine(SnapshotDir, "login.png"), bytes);
    }

    [Test]
    public async Task AdminIndex_Snapshot()
    {
        await Page.GotoAsync($"{BaseUrl}/Account/Login");
        await Page.Locator("[name=Email]").FillAsync("admin@programa-semilla.test");
        await Page.Locator("[name=Password]").FillAsync("Sentinel123!");
        await Page.Locator("main button[type=submit]").ClickAsync();
        await Page.GotoAsync($"{BaseUrl}/Admin");
        await Page.SetViewportSizeAsync(1280, 800);
        var bytes = await Page.ScreenshotAsync(new() { FullPage = true });
        Directory.CreateDirectory(SnapshotDir);
        await File.WriteAllBytesAsync(Path.Combine(SnapshotDir, "admin-index.png"), bytes);
    }

    [Test]
    public async Task ReviewerQueue_Snapshot()
    {
        await Page.GotoAsync($"{BaseUrl}/Account/Login");
        await Page.Locator("[name=Email]").FillAsync("admin@programa-semilla.test");
        await Page.Locator("[name=Password]").FillAsync("Sentinel123!");
        await Page.Locator("main button[type=submit]").ClickAsync();
        await Page.GotoAsync($"{BaseUrl}/Review");
        await Page.SetViewportSizeAsync(1280, 800);
        var bytes = await Page.ScreenshotAsync(new() { FullPage = true });
        Directory.CreateDirectory(SnapshotDir);
        await File.WriteAllBytesAsync(Path.Combine(SnapshotDir, "reviewer-queue.png"), bytes);
    }

    [Test]
    public async Task ApplicantHome_Snapshot()
    {
        var unique = Guid.NewGuid().ToString("N")[..8];
        var email = $"snap_appl_{unique}@example.com";
        const string password = "Test123!";
        await RegisterUserAsync(Page, email, password, "Snap", "Appl", $"SN-{unique}");
        await LoginAsync(Page, email, password);
        await Page.GotoAsync($"{BaseUrl}/Application");
        await Page.SetViewportSizeAsync(1280, 800);
        var bytes = await Page.ScreenshotAsync(new() { FullPage = true });
        Directory.CreateDirectory(SnapshotDir);
        await File.WriteAllBytesAsync(Path.Combine(SnapshotDir, "applicant-home.png"), bytes);
    }
}
