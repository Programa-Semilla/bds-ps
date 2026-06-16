using System.Text.RegularExpressions;
using FundingPlatform.Tests.E2E.Constants;
using FundingPlatform.Tests.E2E.Fixtures;
using FundingPlatform.Tests.E2E.PageObjects;
using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.Tests;

/// <summary>
/// Spec 035 / US4 / T054 — every application-render surface shows each line item's
/// category field values + per-item impact. Covers the applicant Details and the
/// pre-submit Review surfaces, and the reviewer detail page. The funding-agreement
/// PDF per-line block (T059) is exercised by the rebuilt FundingAgreement PDF suite,
/// which now routes through the per-item item form via the shared base helper.
/// </summary>
public class LineItemDisplayTests : AuthenticatedTestBase
{
    private string _quotationFile = string.Empty;

    [SetUp]
    public void SetUpQuotationFile()
    {
        _quotationFile = Path.Combine(Path.GetTempPath(), $"disp-quote-{Guid.NewGuid():N}.pdf");
        File.WriteAllText(_quotationFile, "%PDF-1.4 display placeholder");
    }

    [TearDown]
    public void DeleteQuotationFile()
    {
        if (File.Exists(_quotationFile)) File.Delete(_quotationFile);
    }

    private async Task<List<int>> GetItemIdsAsync(int appId)
    {
        await Page.GotoAsync($"{BaseUrl}/Application/Edit/{appId}");
        var links = Page.Locator("[data-testid=application-edit-item-row] a:has-text('Editar')");
        var n = await links.CountAsync();
        var ids = new List<int>();
        for (var i = 0; i < n; i++)
        {
            var href = await links.Nth(i).GetAttributeAsync("href") ?? string.Empty;
            var m = Regex.Match(href, @"/Item/(\d+)/Edit");
            if (m.Success) ids.Add(int.Parse(m.Groups[1].Value));
        }
        return ids;
    }

    /// <summary>
    /// Builds a draft with one line item whose first (text) category field carries a
    /// unique marker, plus a per-item impact template. Returns the app id + marker.
    /// </summary>
    private async Task<(int appId, string marker)> BuildMarkedDraftAsync(string prefix, bool withQuotations)
    {
        var uniqueId = Guid.NewGuid().ToString("N")[..8];
        var email = $"{prefix}_{uniqueId}@example.com";
        await RegisterUserAsync(Page, email, "Test123!", "Disp", "Tester", $"DID-{uniqueId}");
        await LoginAsync(Page, email, "Test123!");

        var appPage = new ApplicationPage(Page);
        await appPage.GotoListAsync(BaseUrl);
        await appPage.CreateApplicationAsync();
        var appId = int.Parse(Regex.Match(Page.Url, @"/Application/Edit/(\d+)").Groups[1].Value);

        // Spec 035 (evolved) — declare an impact at the application level first.
        var impactsPage = new ApplicationImpactsPage(Page);
        await impactsPage.GotoAsync(appId, BaseUrl);
        await impactsPage.AddImpactAsync(0);

        var marker = $"MARCA-{uniqueId}";
        var itemPage = new ItemPage(Page);
        await Page.GotoAsync($"{BaseUrl}/Application/{appId}/Item/Add");
        await itemPage.SelectCategoryAndFillFieldsAsync(0);
        await itemPage.CategoryFieldsContainer.Locator("input[type=text][data-dynamic-field]").First.FillAsync(marker);
        await itemPage.ProductNameInput.FillAsync($"Producto {uniqueId}");
        await itemPage.AttributeFirstImpactAndJustifyAsync($"Justificación {uniqueId}");
        await itemPage.SubmitButton.ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex(@"/Application/Edit/\d+"));

        if (withQuotations)
        {
            var ids = await GetItemIdsAsync(appId);
            for (var i = 1; i <= 2; i++)
            {
                var supplier = new SupplierPage(Page);
                await supplier.NavigateToAddAsync(appId, ids[0], BaseUrl);
                await supplier.FillSupplierFormAsync(
                    $"DQ{i}-{appId}", $"Prov {i} {appId}", 800m * i, "2027-12-31", _quotationFile);
                await supplier.SubmitAsync();
                await Expect(Page).ToHaveURLAsync(new Regex(@"/Application/Edit/\d+"));
            }
        }

        return (appId, marker);
    }

    [Test]
    public async Task ApplicantDetailsAndReview_ShowPerItemCategoryAndImpact()
    {
        var (appId, marker) = await BuildMarkedDraftAsync("disp_applicant", withQuotations: false);

        // Applicant Details — per-item category values + impact render inside the item block.
        await Page.GotoAsync($"{BaseUrl}/Application/Details/{appId}");
        await Expect(Page.Locator("[data-testid^=item-category-fields-]").First).ToContainTextAsync(marker);
        await Expect(Page.Locator("[data-testid^=item-impact-]").First).ToBeVisibleAsync();

        // Pre-submit Review — reachable via the now-open submit gate.
        await Page.GotoAsync($"{BaseUrl}/Application/Edit/{appId}");
        var draft = new ApplicationDraftPage(Page);
        await Expect(draft.SubmitButton).ToBeEnabledAsync();
        await draft.GoToReviewAsync();
        await Expect(Page).ToHaveURLAsync(new Regex(@"/Applications/.+/Review"));
        await Expect(Page.Locator("[data-testid^=review-item-detail-]").First).ToContainTextAsync(marker);
    }

    [Test]
    public async Task ReviewerDetail_ShowsPerItemCategoryAndImpact()
    {
        var (appId, marker) = await BuildMarkedDraftAsync("disp_reviewer", withQuotations: true);

        // Submit through the review confirmation (2 quotations meet the min).
        await SubmitDraftViaReviewAsync(appId);
        await Expect(Page.Locator($"[data-testid=status-pill]:has-text('{UiCopy.State.Submitted}')")).ToBeVisibleAsync();
        await Page.Locator("form[action*='Account/Logout'] button[type=submit]").ClickAsync();

        // Reviewer (member of all seeded groups) opens the application detail.
        var uid = Guid.NewGuid().ToString("N")[..8];
        var reviewerEmail = $"disp_rev_{uid}@example.com";
        await RegisterUserAsync(Page, reviewerEmail, "Test123!", "Rev", "Tester", $"REV-{uid}");
        await AssignRoleAsync(reviewerEmail, "Reviewer");
        await LoginAsync(Page, reviewerEmail, "Test123!");

        var reviewPage = new ReviewApplicationPage(Page);
        await reviewPage.GotoAsync(BaseUrl, appId);

        await Expect(Page.Locator("[data-testid=review-item-category-fields]").First).ToContainTextAsync(marker);
        await Expect(Page.GetByText("Evaluación de impacto").First).ToBeVisibleAsync();
    }
}
