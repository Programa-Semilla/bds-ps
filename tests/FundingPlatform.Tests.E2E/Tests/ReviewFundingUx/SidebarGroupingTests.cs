using FundingPlatform.Tests.E2E.Fixtures;
using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.Tests.ReviewFundingUx;

/// <summary>
/// Spec 027 / US8 (FR-022/FR-023, SC-007) — the sidebar regroups into
/// Inicio / Administración / Proceso with zero removals and role-gating
/// preserved. Every prior destination stays reachable under the right group.
/// </summary>
[Category("ReviewFundingUx")]
public class SidebarGroupingTests : AuthenticatedTestBase
{
    private const string Password = "Test123!";

    private ILocator Entry(string slug) => Page.Locator($"[data-testid=sidebar-entry-{slug}]");

    private async Task RegisterAndLoginAsync(string slug, string? role)
    {
        var unique = Guid.NewGuid().ToString("N")[..8];
        var email = $"sb_{slug}_{unique}@example.com";
        await RegisterUserAsync(Page, email, Password, "Sidebar", slug, $"SB-{unique}");
        if (role is not null) await AssignRoleAsync(email, role);
        await LoginAsync(Page, email, Password);
        await Page.GotoAsync($"{BaseUrl}/");
    }

    [Test]
    public async Task Admin_SeesThreeGroups_AllDestinationsReachable()
    {
        await RegisterAndLoginAsync("admin", "Admin");

        // Two section headers (Inicio is headerless top-level).
        await Expect(Page.Locator("[data-section-testid=admin-section]")).ToBeVisibleAsync();
        await Expect(Page.Locator("[data-section-testid=proceso-section]")).ToBeVisibleAsync();

        // Inicio group (top-level, reviewer items visible to admin too).
        await Expect(Entry("home")).ToBeVisibleAsync();
        await Expect(Entry("review-queue")).ToBeVisibleAsync();
        await Expect(Entry("generate-agreement")).ToBeVisibleAsync();
        await Expect(Entry("signing-inbox")).ToBeVisibleAsync();

        // Administración group.
        foreach (var slug in new[] { "suppliers", "plantillas", "reports", "currencies", "exchange-rates", "users", "system-config" })
        {
            await Expect(Entry(slug)).ToBeVisibleAsync();
        }

        // Proceso group (header links to /Admin/Processes; children incl. Starters).
        Assert.That(await Page.Locator("[data-section-testid=proceso-section]").GetAttributeAsync("href"),
            Is.EqualTo("/Admin/Processes"));
        foreach (var slug in new[] { "groups", "starters", "impact-templates", "legacy-quotations" })
        {
            await Expect(Entry(slug)).ToBeVisibleAsync();
        }
    }

    [Test]
    public async Task Admin_Starters_OpensApplicationsListing()
    {
        await RegisterAndLoginAsync("starter", "Admin");

        Assert.That(await Entry("starters").GetAttributeAsync("href"),
            Is.EqualTo("/Admin/Reports/Applications"));
        await Entry("starters").ClickAsync();
        await Expect(Page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex(@"/Admin/Reports/Applications"));
    }

    [Test]
    public async Task Reviewer_SeesInicioItems_NoAdminOrProcesoSections()
    {
        await RegisterAndLoginAsync("reviewer", "Reviewer");

        await Expect(Entry("review-queue")).ToBeVisibleAsync();
        await Expect(Entry("generate-agreement")).ToBeVisibleAsync();
        await Expect(Entry("signing-inbox")).ToBeVisibleAsync();

        Assert.That(await Page.Locator("[data-section-testid=admin-section]").CountAsync(), Is.EqualTo(0));
        Assert.That(await Page.Locator("[data-section-testid=proceso-section]").CountAsync(), Is.EqualTo(0));
    }

    [Test]
    public async Task Applicant_SeesMyApplications_NoAdminOrProcesoSections()
    {
        await RegisterAndLoginAsync("applicant", role: null);

        await Expect(Entry("my-applications")).ToBeVisibleAsync();
        Assert.That(await Page.Locator("[data-section-testid=proceso-section]").CountAsync(), Is.EqualTo(0));
        Assert.That(await Entry("users").CountAsync(), Is.EqualTo(0));
    }

    [Test]
    public async Task SupplierAdmin_SeesNarrowedVariant_NoProcesoSection()
    {
        await RegisterAndLoginAsync("supplieradmin", "SupplierAdmin");

        await Expect(Page.Locator("[data-testid=sidebar-supplier-admin-variant]")).ToBeVisibleAsync();
        await Expect(Entry("supplier-admin-suppliers")).ToBeVisibleAsync();
        Assert.That(await Page.Locator("[data-section-testid=proceso-section]").CountAsync(), Is.EqualTo(0));
        Assert.That(await Page.Locator("[data-section-testid=admin-section]").CountAsync(), Is.EqualTo(0));
    }
}
