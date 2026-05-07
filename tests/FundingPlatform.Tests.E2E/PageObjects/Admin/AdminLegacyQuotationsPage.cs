using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.PageObjects.Admin;

/// <summary>
/// Spec 015 / US6 / T602 — page object for the admin "Cotizaciones Pendientes"
/// queue at <c>/Admin/AdminLegacyQuotations</c>.
/// </summary>
public class AdminLegacyQuotationsPage : AdminBasePage
{
    public AdminLegacyQuotationsPage(IPage page) : base(page) { }

    public ILocator Table => Page.Locator("[data-testid=\"admin-legacy-quotations-table\"]");
    public new ILocator EmptyState => Page.Locator("[data-testid=\"admin-legacy-quotations-empty\"]");
    public ILocator SuccessBanner => Page.Locator("[data-testid=\"success-banner\"]");
    public ILocator ErrorBanner => Page.Locator("[data-testid=\"error-banner\"]");
    public ILocator AnyRow => Page.Locator("[data-testid^=\"admin-legacy-quotation-row-\"]");

    public ILocator RowFor(int quotationId) =>
        Page.Locator($"[data-testid=\"admin-legacy-quotation-row-{quotationId}\"]");

    public ILocator RateSelect(int quotationId) =>
        RowFor(quotationId).Locator("[data-testid=\"legacy-quotation-rate-select\"]");

    public ILocator AttachButton(int quotationId) =>
        RowFor(quotationId).Locator("[data-testid=\"legacy-quotation-attach-button\"]");

    public Task GoToAsync(string baseUrl) =>
        Page.GotoAsync($"{baseUrl}/Admin/AdminLegacyQuotations");
}
