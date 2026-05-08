using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.PageObjects.Admin;

/// <summary>
/// Spec 017 / US1 — semantic POM for the new admin dashboard.
/// Exposes KPI tiles, capability cards, section headers, and the activity
/// feed via stable English data-testid slugs.
/// </summary>
public sealed class AdminDashboardPage : BasePage
{
    public AdminDashboardPage(IPage page) : base(page) { }

    public ILocator Root => Page.Locator("[data-testid=admin-dashboard]");
    public ILocator KpiStrip => Page.Locator("[data-testid=admin-kpi-strip]");

    public ILocator Kpi(string slug) => Page.Locator($"[data-testid=admin-kpi-{slug}]");
    public ILocator KpiNumeric(string slug) =>
        Kpi(slug).Locator("[data-testid=kpi-tile-numeric]");

    public ILocator CapabilityCard(string slug) =>
        Page.Locator($"[data-testid=admin-capability-{slug}]");
    public ILocator CapabilityCta(string slug) =>
        Page.Locator($"[data-testid=admin-capability-cta-{slug}]");

    public ILocator Section(string slug) =>
        Page.Locator($"[data-testid=admin-section-{slug}]");

    public ILocator ActivityFeed => Page.Locator("[data-testid=admin-activity-feed]");
    public ILocator ActivityEvents => Page.Locator("[data-testid=admin-activity-event]");

    public Task GotoAsync(string baseUrl) =>
        Page.GotoAsync($"{baseUrl}/Admin");

    public async Task<int?> ReadKpiNumericAsync(string slug)
    {
        // Read the immutable data-ticker-target attribute rather than InnerText.
        // motion.js (wwwroot/js/motion.js) animates the displayed digit from 0
        // to the target value over --motion-slow on every mount, so InnerText
        // is non-deterministic until the animation finishes. The attribute
        // carries the projected final value and is the only stable source.
        var node = KpiNumeric(slug);
        var attr = await node.GetAttributeAsync("data-ticker-target");
        if (int.TryParse(attr,
                System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out var n))
        {
            return n;
        }
        return null;
    }

    public async Task<bool> IsActivityFeedVisibleAsync()
    {
        return await ActivityFeed.IsVisibleAsync();
    }
}
