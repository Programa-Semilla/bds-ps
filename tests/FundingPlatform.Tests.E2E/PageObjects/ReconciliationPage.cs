using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.PageObjects;

/// <summary>
/// Spec 048 / US3 — POM for the reconciliation dashboard: the index (/Reconciliation) with summary
/// tiles + filter toolbar + list, and the per-discrepancy detail (/Reconciliation/{id}) with the
/// fields, correction-history timeline, and the Financial-Operator write affordances.
/// </summary>
public sealed class ReconciliationPage : BasePage
{
    public ReconciliationPage(IPage page) : base(page) { }

    // ---- Index ----
    public ILocator Summary => Page.Locator("[data-testid=reconciliation-summary]");
    public ILocator TileBlockingCount => Page.Locator("[data-testid=tile-blocking-count]");
    public ILocator TileWarningCount => Page.Locator("[data-testid=tile-warning-count]");
    public ILocator List => Page.Locator("[data-testid=reconciliation-list]");
    public ILocator Rows => Page.Locator("[data-testid=reconciliation-row]");
    public ILocator Empty => Page.Locator("[data-testid=reconciliation-empty]");

    public async Task GotoAsync(string baseUrl)
        => await Page.GotoAsync($"{baseUrl}/Reconciliation");

    public ILocator RowsBySeverity(string severity)
        => Page.Locator($"[data-testid=reconciliation-row][data-severity=\"{severity}\"]");

    public async Task ApplySeverityFilterAsync(string severity)
    {
        await Page.Locator("[data-testid=filter-severity]").SelectOptionAsync(severity);
        await Page.Locator("[data-testid=filter-apply]").ClickAsync();
    }

    public async Task OpenFirstAsync()
        => await Rows.First.Locator("[data-testid=reconciliation-open]").ClickAsync();

    // ---- Detail ----
    public ILocator Detail => Page.Locator("[data-testid=reconciliation-detail]");
    public ILocator DetailState => Page.Locator("[data-testid=detail-state]");
    public ILocator Expected => Page.Locator("[data-testid=detail-expected]");
    public ILocator Actual => Page.Locator("[data-testid=detail-actual]");
    public ILocator Difference => Page.Locator("[data-testid=detail-difference]");
    public ILocator RequiredAction => Page.Locator("[data-testid=detail-required-action]");
    public ILocator Timeline => Page.Locator("[data-testid=detail-timeline]");
    public ILocator TimelineEvents => Page.Locator("[data-testid=timeline-event]");
    public ILocator ReadOnlyNotice => Page.Locator("[data-testid=reconciliation-readonly]");
    public ILocator AssignForm => Page.Locator("[data-testid=assign-form]");
    public ILocator WaiveForm => Page.Locator("[data-testid=waive-form]");

    public async Task GotoDetailAsync(string baseUrl, int discrepancyId)
        => await Page.GotoAsync($"{baseUrl}/Reconciliation/{discrepancyId}");

    public async Task AssignFirstAsync()
    {
        await AssignForm.Locator("[data-testid=assign-submit]").ClickAsync();
    }

    /// <summary>Selects the assignee option by its exact visible label, then submits.</summary>
    public async Task AssignToLabelAsync(string label)
    {
        await AssignForm.Locator("[data-testid=assign-assignee]")
            .SelectOptionAsync(new SelectOptionValue { Label = label });
        await AssignForm.Locator("[data-testid=assign-submit]").ClickAsync();
    }

    public async Task MarkUnderCorrectionAsync()
    {
        await Page.Locator("[data-testid=undercorrection-submit]").ClickAsync();
    }

    public async Task WaiveAsync(string reason)
    {
        await WaiveForm.Locator("[data-testid=waive-reason]").FillAsync(reason);
        await WaiveForm.Locator("[data-testid=waive-submit]").ClickAsync();
    }
}
