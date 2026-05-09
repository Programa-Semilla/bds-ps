using FundingPlatform.Tests.E2E.Constants;
using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.PageObjects;

public class ApplicationPage : BasePage
{
    public ApplicationPage(IPage page) : base(page)
    {
    }

    public ILocator CreateButton => Page.Locator("a[href*='Application/Create']").First;
    public ILocator SubmitDraftButton => Page.Locator("[data-testid=application-create-submit]");
    public ILocator CompanyNameInput => Page.Locator("[data-testid=application-create-company-name]");
    public ILocator CompanyNameError => Page.Locator("[data-testid=application-create-company-name-error]");
    public ILocator ApplicationsTable => Page.Locator("table");
    public ILocator AddItemButton => Page.Locator($"a:has-text('{UiCopy.AddItem}')").First;
    public ILocator SubmitApplicationButton => Page.Locator($"button[type=submit]:has-text('{UiCopy.SubmitApplication}')");
    public ILocator StatusBadge => Page.Locator(".badge");
    public ILocator ItemRows => Page.Locator("table tbody tr");

    public async Task GotoListAsync(string baseUrl)
    {
        await Page.GotoAsync($"{baseUrl}/Application");
    }

    /// <summary>
    /// Spec 018 / FR-015 — fills the new CompanyName input and submits the
    /// Create form. Existing tests that just clicked "Create draft" now have
    /// to thread a name through; defaults to a deterministic test value so
    /// every legacy E2E test gets a non-blank string without rewriting
    /// every call site.
    /// </summary>
    public async Task CreateApplicationAsync(string companyName = "Test Company")
    {
        await CreateButton.ClickAsync();
        await CompanyNameInput.FillAsync(companyName);
        await SubmitDraftButton.ClickAsync();
    }

    public async Task ViewApplicationAsync(int id)
    {
        await Page.Locator($"a[href*='Application/Details/{id}'], a[href*='Application/{id}']").First.ClickAsync();
    }
}
