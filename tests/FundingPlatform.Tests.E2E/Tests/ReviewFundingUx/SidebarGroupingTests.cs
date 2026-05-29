using FundingPlatform.Tests.E2E.Fixtures;
using FundingPlatform.Tests.E2E.PageObjects;
using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.Tests.ReviewFundingUx;

/// <summary>
/// Spec 027 / US8 (FR-022/FR-023, SC-007) — the sidebar regroups into
/// Inicio / Operativo / Administración / Proceso with zero removals and
/// role-gating preserved. Every prior destination stays reachable under the
/// right group. Operativo is admin-only: non-admin reviewers keep the
/// operational queues flat at the top level.
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
    public async Task Admin_SeesGroupedSidebar_AllDestinationsReachable()
    {
        await RegisterAndLoginAsync("admin", "Admin");
        var basePage = new ApplicationPage(Page);

        // Three collapsable section headers (Inicio is headerless top-level).
        await Expect(Page.Locator("[data-section-testid=operativo-section]")).ToBeVisibleAsync();
        await Expect(Page.Locator("[data-section-testid=admin-section]")).ToBeVisibleAsync();
        await Expect(Page.Locator("[data-section-testid=proceso-section]")).ToBeVisibleAsync();

        // Inicio group (top-level). For admins the operational queues are NOT here —
        // they move into the Operativo group below; only Inicio stays top-level.
        await Expect(Entry("home")).ToBeVisibleAsync();

        // Operativo group — admin-only accordion holding the operational queues.
        await basePage.ExpandSidebarSectionAsync("operativo-section");
        await Expect(Entry("review-queue")).ToBeVisibleAsync();
        await Expect(Entry("generate-agreement")).ToBeVisibleAsync();
        await Expect(Entry("signing-inbox")).ToBeVisibleAsync();

        // Administración group — expand to reveal children (incl. the Panel landing
        // that keeps the /Admin dashboard reachable now that the header is a toggle).
        await basePage.ExpandSidebarSectionAsync("admin-section");
        foreach (var slug in new[] { "admin-home", "suppliers", "plantillas", "reports", "currencies", "exchange-rates", "users", "system-config" })
        {
            await Expect(Entry(slug)).ToBeVisibleAsync();
        }

        // Proceso group — the header is a toggle; its /Admin/Processes destination
        // is preserved as the "processes" landing child. Children incl. Starters.
        await basePage.ExpandSidebarSectionAsync("proceso-section");
        Assert.That(await Entry("processes").GetAttributeAsync("href"),
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
        var basePage = new ApplicationPage(Page);

        // Starters lives under the Proceso group — expand it first (real journey).
        await basePage.ExpandSidebarSectionAsync("proceso-section");
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

        // Non-admin reviewers keep the operational queues flat — no Operativo group.
        Assert.That(await Page.Locator("[data-section-testid=operativo-section]").CountAsync(), Is.EqualTo(0));
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
