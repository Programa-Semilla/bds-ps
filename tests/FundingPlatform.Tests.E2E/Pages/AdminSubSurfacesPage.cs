using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.Pages;

/// <summary>
/// Spec 019 T058 / FR-027 / FR-032 — Admin sub-surface POMs (consolidated into a
/// single class since the admin views share chrome). Each property navigates the
/// matching admin sub-route.
/// </summary>
public class AdminSubSurfacesPage
{
    private readonly IPage _page;

    public AdminSubSurfacesPage(IPage page)
    {
        _page = page;
    }

    public Task GotoUsersAsync(string baseUrl) => _page.GotoAsync($"{baseUrl}/Admin/Users");
    public Task GotoGroupsAsync(string baseUrl) => _page.GotoAsync($"{baseUrl}/Admin/Groups");
    public Task GotoSuppliersAsync(string baseUrl) => _page.GotoAsync($"{baseUrl}/Admin/Suppliers");
    public Task GotoReportsAsync(string baseUrl) => _page.GotoAsync($"{baseUrl}/Admin/Reports");
    public Task GotoCurrenciesAsync(string baseUrl) => _page.GotoAsync($"{baseUrl}/Admin/Currencies");
    public Task GotoExchangeRatesAsync(string baseUrl) => _page.GotoAsync($"{baseUrl}/Admin/ExchangeRates");
    public Task GotoLegacyQuotationsAsync(string baseUrl) => _page.GotoAsync($"{baseUrl}/Admin/LegacyQuotations");
    public Task GotoConfigurationAsync(string baseUrl) => _page.GotoAsync($"{baseUrl}/Admin/Configuration");
    public Task GotoImpactTemplatesAsync(string baseUrl) => _page.GotoAsync($"{baseUrl}/Admin/ImpactTemplates");

    public ILocator BrandSidebar => _page.Locator("[data-testid=\"sidebar-brand\"]");
    public ILocator SponsorStrip => _page.Locator("[data-testid=\"sponsor-strip\"]");
    public ILocator MainTable => _page.Locator(".fl-table").First;
    public ILocator ReportsActiveChip => _page.Locator(".fl-chip[aria-pressed=\"true\"]").First;
    public ILocator ReportsAllChips => _page.Locator(".fl-chip");
}
