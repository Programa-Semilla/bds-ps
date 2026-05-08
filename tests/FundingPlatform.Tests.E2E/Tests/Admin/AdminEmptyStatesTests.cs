using FundingPlatform.Tests.E2E.Fixtures;
using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.Tests.Admin;

/// <summary>
/// Spec 017 / US3 / FR-012 — verifies that admin tables render the spec-011
/// 9-illustration set in their empty branches. Each surface maps to exactly
/// one scene key per the empty-state coverage table in
/// ADMIN-SWEEP-CHECKLIST.md.
/// </summary>
public class AdminEmptyStatesTests : AuthenticatedTestBase
{
    private async Task RegisterAndLoginAsAdminAsync(IPage page, string email, string password)
    {
        await RegisterUserAsync(page, email, password, "Admin", "Tester", $"LID-{Guid.NewGuid():N}"[..16]);
        await AssignRoleAsync(email, "Admin");
        await LoginAsync(page, email, password);
    }

    [SetUp]
    public Task ResetFixtureAsync() => ResetAdminFixtureAsync();

    [TearDown]
    public Task RestoreSeededFixtureAsync() => SeedAdminFixtureAsync();

    private async Task ExpectSceneAsync(string testidContainer, string expectedSceneKey)
    {
        // Anchor on the surface's own empty-state container so we don't pick up
        // an unrelated empty illustration elsewhere on the page.
        var illustration = Page.Locator($"[data-testid={testidContainer}] [data-testid=empty-state-illustration]");
        await Expect(illustration).ToBeVisibleAsync();
        var scene = await illustration.GetAttributeAsync("data-scene");
        Assert.That(scene, Is.EqualTo(expectedSceneKey),
            $"Expected scene '{expectedSceneKey}' for surface anchored at [data-testid={testidContainer}].");
    }

    [Test]
    public async Task GroupsIndex_NoGroupsYet_RendersFoldersStackScene()
    {
        var uniqueId = Guid.NewGuid().ToString("N")[..8];
        await RegisterAndLoginAsAdminAsync(Page, $"admin_empty_groups_{uniqueId}@example.com", "Test123!");
        await Page.GotoAsync($"{BaseUrl}/Admin/Groups");

        await ExpectSceneAsync("admin-groups-empty", "folders-stack");
    }

    [Test]
    public async Task SuppliersIndex_NoSuppliers_RendersFoldersStackScene()
    {
        var uniqueId = Guid.NewGuid().ToString("N")[..8];
        await RegisterAndLoginAsAdminAsync(Page, $"admin_empty_supp_{uniqueId}@example.com", "Test123!");
        await Page.GotoAsync($"{BaseUrl}/Admin/Suppliers");

        await ExpectSceneAsync("admin-suppliers-empty", "folders-stack");
    }

    [Test]
    public async Task SuppliersIndex_FilteredNoResults_RendersMagnifierOnEmpty()
    {
        var uniqueId = Guid.NewGuid().ToString("N")[..8];
        await RegisterAndLoginAsAdminAsync(Page, $"admin_filt_supp_{uniqueId}@example.com", "Test123!");

        // hasIncompleteCompliance=true with zero matching suppliers triggers
        // the filtered-no-results branch (ResetAdminFixtureAsync flips every
        // remaining supplier to fully compliant), which must render the
        // magnifier scene rather than the folders-stack one.
        await Page.GotoAsync($"{BaseUrl}/Admin/Suppliers?hasIncompleteCompliance=true");

        await ExpectSceneAsync("admin-suppliers-empty", "magnifier-on-empty");
    }

    [Test]
    public async Task LegacyQuotationsIndex_NoLegacy_RendersCalmHorizonScene()
    {
        var uniqueId = Guid.NewGuid().ToString("N")[..8];
        await RegisterAndLoginAsAdminAsync(Page, $"admin_empty_lq_{uniqueId}@example.com", "Test123!");
        await Page.GotoAsync($"{BaseUrl}/Admin/LegacyQuotations");

        await ExpectSceneAsync("admin-legacy-quotations-empty", "calm-horizon");
    }

    [Test]
    public async Task ImpactTemplatesIndex_NoTemplates_RendersFoldersStackScene()
    {
        var uniqueId = Guid.NewGuid().ToString("N")[..8];
        await RegisterAndLoginAsAdminAsync(Page, $"admin_empty_it_{uniqueId}@example.com", "Test123!");
        await Page.GotoAsync($"{BaseUrl}/Admin/ImpactTemplates");

        // ImpactTemplates view has no surface-anchored container around the
        // empty state; assert directly on the illustration.
        var illustration = Page.Locator("[data-testid=empty-state-illustration]");
        await Expect(illustration).ToBeVisibleAsync();
        var scene = await illustration.GetAttributeAsync("data-scene");
        Assert.That(scene, Is.EqualTo("folders-stack"));
    }
}
