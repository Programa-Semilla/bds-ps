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
    // Spec 037 — company is a controlled selector (select when multi, hidden when single).
    public ILocator CompanySelect => Page.Locator("select[data-testid=application-create-company]");
    public ILocator CompanyError => Page.Locator("[data-testid=application-create-company-error]");
    public ILocator NoCompaniesBlock => Page.Locator("[data-testid=application-create-no-companies]");
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
    /// Spec 037 / FR-002 — opens the Create form, picks a company (when a multi-company
    /// selector is rendered; single-company applicants auto-select via a hidden field),
    /// anchors the eligible group, and submits. The legacy <c>companyName</c> parameter
    /// is retained for call-site compatibility but is ignored — selection is by id now.
    /// </summary>
    public async Task CreateApplicationAsync(string companyName = "Test Company")
    {
        await CreateButton.ClickAsync();
        await SelectCompanyIfPresentAsync();
        await SelectEligibleGroupIfPresentAsync();
        await SubmitDraftButton.ClickAsync();
    }

    /// <summary>
    /// Spec 037 — when a multi-company selector is rendered, pick the first real option.
    /// Single-company applicants render a hidden auto-selected field (this locator matches
    /// nothing) so we skip.
    /// </summary>
    public async Task SelectCompanyIfPresentAsync()
    {
        if (await CompanySelect.CountAsync() > 0)
        {
            await CompanySelect.SelectOptionAsync(new SelectOptionValue { Index = 1 });
        }
    }

    /// <summary>
    /// Spec 029 / FR-018 — the create form anchors the application to an eligible
    /// Group (Process/convocatoria). When the applicant is a member of ≥2 eligible
    /// groups (the E2E default — RegisterUserAsync assigns all seeded groups), a
    /// required <c>&lt;select&gt;</c> is rendered; pick the first real option. With
    /// exactly one eligible group the control is a hidden input (auto-anchored) and
    /// this locator matches nothing, so we skip. Call this in custom create flows
    /// after clicking Create + filling the company name, before submitting.
    /// </summary>
    public async Task SelectEligibleGroupIfPresentAsync()
    {
        var groupSelect = Page.Locator("select[data-testid=application-create-group]");
        if (await groupSelect.CountAsync() > 0)
        {
            await groupSelect.SelectOptionAsync(new SelectOptionValue { Index = 1 });
        }
    }

    public async Task ViewApplicationAsync(int id)
    {
        await Page.Locator($"a[href*='Application/Details/{id}'], a[href*='Application/{id}']").First.ClickAsync();
    }
}
