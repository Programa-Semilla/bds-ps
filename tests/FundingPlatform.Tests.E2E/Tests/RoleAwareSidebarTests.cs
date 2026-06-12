using FundingPlatform.Tests.E2E.Fixtures;
using FundingPlatform.Tests.E2E.PageObjects;
using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.Tests;

public class RoleAwareSidebarTests : AuthenticatedTestBase
{
    private static readonly string[] _allEntries =
    {
        "home",
        "my-applications",
        "review-queue",
        "signing-inbox",
        "admin",
    };

    private async Task RegisterAndLoginAsync(string roleSlug, string? role)
    {
        var unique = Guid.NewGuid().ToString("N")[..8];
        var email = $"sidebar_{roleSlug}_{unique}@example.com";
        const string password = "Test123!";
        await RegisterUserAsync(Page, email, password, "Sidebar", roleSlug, $"SID-{unique}");
        if (role is not null)
        {
            await AssignRoleAsync(email, role);
        }
        await LoginAsync(Page, email, password);
    }

    private async Task AssertEntriesAsync(string[] expected)
    {
        var basePage = new ApplicationPage(Page);
        await Expect(basePage.Sidebar).ToBeVisibleAsync();

        foreach (var slug in _allEntries)
        {
            var entry = basePage.SidebarEntry(slug);
            if (expected.Contains(slug))
            {
                Assert.That(await entry.CountAsync(), Is.GreaterThan(0),
                    $"Sidebar entry '{slug}' should be visible for this role.");
            }
            else
            {
                Assert.That(await entry.CountAsync(), Is.EqualTo(0),
                    $"Sidebar entry '{slug}' must NOT be visible for this role.");
            }
        }
    }

    [Test]
    public async Task ApplicantSidebarShowsApplicantEntries()
    {
        await RegisterAndLoginAsync("Applicant", role: null);
        await Page.GotoAsync(BaseUrl + "/");
        await AssertEntriesAsync(new[] { "home", "my-applications" });
    }

    [Test]
    public async Task ReviewerSidebarShowsReviewerEntries()
    {
        await RegisterAndLoginAsync("Reviewer", role: "Reviewer");
        await Page.GotoAsync(BaseUrl + "/");
        await AssertEntriesAsync(new[] { "home", "review-queue", "signing-inbox" });
    }

    [Test]
    public async Task AdminSidebarShowsAdminEntries()
    {
        await RegisterAndLoginAsync("Admin", role: "Admin");
        await Page.GotoAsync(BaseUrl + "/");
        await AssertEntriesAsync(new[] { "home", "review-queue", "signing-inbox", "admin" });
    }

    [Test]
    public async Task UnauthenticatedAuthShellOmitsSidebar()
    {
        var loginPage = new LoginPage(Page);
        await loginPage.GotoAsync(BaseUrl);
        Assert.That(await loginPage.Sidebar.CountAsync(), Is.EqualTo(0),
            "Unauthenticated Login page must not render the sidebar.");
        Assert.That(await loginPage.IsAuthShellVisibleAsync(), Is.True,
            "Unauthenticated Login page must render the auth shell.");
        // Spec 032 — the public Register page is removed (returns 404), so the
        // auth-shell-without-sidebar assertion is covered by the Login page alone.
    }
}
