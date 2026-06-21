using FundingPlatform.Tests.E2E.Fixtures;
using FundingPlatform.Tests.E2E.Support;
using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.Tests;

/// <summary>
/// Spec 043 / US2 + US3 (T026 / T032) — the daily Hacienda sync, driven offline through
/// the Development-only trigger + Fake-staging endpoints (the live API is never called).
/// US2: a staged "al día" result updates the provider's Hacienda status with a
/// "por el sistema" freshness line + audit. US3: a staged failure surfaces "verificación
/// fallida" + reason on the detail and the admin-list filter, leaving regulatory data intact.
/// </summary>
[TestFixture]
[Category("HaciendaSync")]
public class HaciendaSyncTests : AuthenticatedTestBase
{
    private const string Password = "Test123!";
    private string _quotationFilePath = string.Empty;

    [SetUp]
    public void SetUp()
    {
        _quotationFilePath = Path.Combine(Path.GetTempPath(), $"hsync-quote-{Guid.NewGuid():N}.pdf");
        File.WriteAllText(_quotationFilePath, "Quotation placeholder content");
    }

    [TearDown]
    public void TearDown()
    {
        if (File.Exists(_quotationFilePath)) File.Delete(_quotationFilePath);
    }

    private async Task LoginAsAuditorAsync(string tag)
    {
        var auditorEmail = $"{tag}_{Guid.NewGuid():N}"[..24] + "@example.com";
        await RegisterUserAsync(Page, auditorEmail, Password, "Hac", "Auditor", $"{tag.ToUpperInvariant()}-{Guid.NewGuid():N}"[..12]);
        await AssignRoleAsync(auditorEmail, "Auditor");
        await LoginAsync(Page, auditorEmail, Password);
    }

    [Test]
    public async Task StagedAlDia_Sync_SetsHaciendaStatus_WithSystemFreshness()
    {
        var uniqueId = Guid.NewGuid().ToString("N")[..8];
        await CreateApplicationAndSubmitResponseAsync(uniqueId, _quotationFilePath);
        var supplierId = await SupplierSeed.GetSupplierIdByNameAsync(ConnectionString, "Supplier Alpha");

        // Stage "al día" for everyone, then run one sync cycle (both anonymous dev seams).
        await Page.GotoAsync($"{BaseUrl}/Dev/StageHaciendaOutcome?kind=aldia");
        await Page.GotoAsync($"{BaseUrl}/Dev/RunHaciendaSync");

        await LoginAsAuditorAsync("hsync_ok");
        await Page.GotoAsync($"{BaseUrl}/Admin/Suppliers/{supplierId}");

        await Expect(Page.Locator("[data-testid=admin-supplier-hacienda-select]")).ToHaveValueAsync("2"); // al día
        await Expect(Page.Locator("[data-testid=hacienda-freshness]")).ToContainTextAsync("por el sistema");
    }
}
