using System.Text.RegularExpressions;
using FundingPlatform.Tests.E2E.Fixtures;
using FundingPlatform.Tests.E2E.PageObjects.Admin;
using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.Tests.Admin;

/// <summary>
/// Spec 017 / US7 — admin index activity feed visible/hidden behaviour. The
/// projection emits FeedVisible=false in zero-of-everything fixtures, so the
/// fresh-admin scenario must NOT render the [data-testid=admin-activity-feed]
/// container. Once a group is created, the feed appears and links the event
/// row back to the group's edit page (per FR-038).
/// </summary>
public class AdminActivityFeedTests : AuthenticatedTestBase
{
    private async Task RegisterAndLoginAsAdminAsync(IPage page, string email, string password)
    {
        await RegisterUserAsync(page, email, password, "Admin", "Tester", $"LID-{Guid.NewGuid():N}"[..16]);
        await AssignRoleAsync(email, "Admin");
        await LoginAsync(page, email, password);
    }

    [TearDown]
    public async Task RestoreSeededFixtureAsync()
    {
        // ZeroOfEverythingFixture_* drops the seeded Groups + ImpactTemplates
        // via ResetAdminFixtureAsync. Re-plant so downstream tests in the
        // shared fixture see the post-deploy seed they expect.
        await SeedAdminFixtureAsync();
    }

    [Test]
    public async Task ZeroOfEverythingFixture_ActivityFeedHidden()
    {
        var uniqueId = Guid.NewGuid().ToString("N")[..8];

        // Wipe AdminAuditEvents (and related state) so FeedVisible=false
        // resolves to no [data-testid=admin-activity-feed] container.
        await ResetAdminFixtureAsync();

        await RegisterAndLoginAsAdminAsync(Page, $"admin_feed_zero_{uniqueId}@example.com", "Test123!");

        var dashboard = new AdminDashboardPage(Page);
        await dashboard.GotoAsync(BaseUrl);

        // Per FR-038 / projection FeedVisible=false in zero-events fixtures —
        // the feed container must not render at all.
        await Expect(dashboard.ActivityFeed).Not.ToBeVisibleAsync();
        Assert.That(await dashboard.ActivityFeed.CountAsync(), Is.EqualTo(0),
            "Activity feed container must be absent (not just invisible) when no AdminAuditEvent rows exist.");
    }

    [Test]
    public async Task AfterCreatingGroup_ActivityFeedVisibleWithDeepLink()
    {
        var uniqueId = Guid.NewGuid().ToString("N")[..8];
        await RegisterAndLoginAsAdminAsync(Page, $"admin_feed_one_{uniqueId}@example.com", "Test123!");

        // Create a group → emits AdminAuditEvent("group.create") which the
        // projection picks up as a FeedVisible=true entry on the next reload.
        await Page.GotoAsync($"{BaseUrl}/Admin/Groups/Create");
        await Page.Locator("[data-testid=admin-group-name-input]").FillAsync($"FeedTestGroup-{uniqueId}");
        await Page.Locator("[data-testid=admin-group-create-submit]").ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex("/Admin/Groups(\\?.*)?$"));

        var dashboard = new AdminDashboardPage(Page);
        await dashboard.GotoAsync(BaseUrl);

        await Expect(dashboard.ActivityFeed).ToBeVisibleAsync();
        Assert.That(await dashboard.ActivityEvents.CountAsync(), Is.GreaterThanOrEqualTo(1),
            "At least one event row should render after creating a group.");

        // Group create events resolve to /Admin/Groups/{id}/Edit deep-links.
        var firstHref = await dashboard.ActivityEvents.First.Locator("a").First.GetAttributeAsync("href");
        Assert.That(firstHref, Does.Contain("/Admin/Groups/"));
    }
}
