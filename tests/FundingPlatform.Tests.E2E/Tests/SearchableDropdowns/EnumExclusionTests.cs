// Spec 031 / FR-009 + SC-006 — static enum dropdowns are excluded from the
// enhancement (no opt-in attribute, no combobox). The Identification-type select
// is an enum explicitly named in FR-009's exclusion list, so it must remain a
// plain native dropdown with no search box.
//
// Spec 032 — public registration was removed; this assertion now runs against the
// same IdentificationType enum select on the admin user-create form.

using FundingPlatform.Tests.E2E.Fixtures;
using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.Tests.SearchableDropdowns;

public class EnumExclusionTests : AuthenticatedTestBase
{
    [Test]
    public async Task EnumDropdown_IsNotEnhanced_NoSearchBox()
    {
        await LoginAsync(Page, "admin@programa-semilla.test", "Sentinel123!");
        await Page.GotoAsync($"{BaseUrl}/Admin/Users/Create");

        // Default role is Applicant, so the identification-type enum select renders.
        var idType = Page.Locator("[name=IdentificationType]");
        await Expect(idType).ToBeVisibleAsync();

        // FR-009 — the enum select is not opted in and is never enhanced.
        Assert.That(await idType.GetAttributeAsync("data-searchable"), Is.Null,
            "An enum dropdown must not carry the data-searchable opt-in.");
        Assert.That(await idType.GetAttributeAsync("data-searchable-enhanced"), Is.Null,
            "An enum dropdown must never be enhanced into a combobox.");

        // The enum select is not replaced by a generated combobox input.
        Assert.That(await Page.Locator("[data-testid=\"IdentificationType-search\"]").CountAsync(), Is.EqualTo(0),
            "The enum select must not be replaced by a searchable combobox input.");
    }
}
