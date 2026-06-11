// Spec 031 / FR-009 + SC-006 — static enum dropdowns are excluded from the
// enhancement (no opt-in attribute, no combobox). The Register form's
// Identification-type select is an enum explicitly named in FR-009's exclusion
// list, so it must remain a plain native dropdown with no search box.

using FundingPlatform.Tests.E2E.Fixtures;
using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.Tests.SearchableDropdowns;

public class EnumExclusionTests : AuthenticatedTestBase
{
    [Test]
    public async Task EnumDropdown_IsNotEnhanced_NoSearchBox()
    {
        await Page.GotoAsync($"{BaseUrl}/Account/Register");

        var idType = Page.Locator("[name=IdentificationType]");
        await Expect(idType).ToBeVisibleAsync();

        // FR-009 — the enum select is not opted in and is never enhanced.
        Assert.That(await idType.GetAttributeAsync("data-searchable"), Is.Null,
            "An enum dropdown must not carry the data-searchable opt-in.");
        Assert.That(await idType.GetAttributeAsync("data-searchable-enhanced"), Is.Null,
            "An enum dropdown must never be enhanced into a combobox.");

        // SC-006 — no combobox input is rendered anywhere on this enum-only form.
        await Expect(Page.Locator(".fl-searchable-input")).ToHaveCountAsync(0);
    }
}
