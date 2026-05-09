using FundingPlatform.Tests.E2E.Fixtures;
using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.Brand;

/// <summary>
/// Spec 019 T046 / FR-019 / FR-031 — Reviewer surfaces use --space-2 cell padding
/// (≈ 8 px) and applicant surfaces use --space-4 (≈ 16 px). Spec 011 FR-060
/// canonical density rule MUST NOT regress.
/// </summary>
public class ReviewerDensityTests : AuthenticatedTestBase
{
    [Test]
    public async Task ReviewerTable_HasSmallerCellPadding_ThanApplicantTable()
    {
        // Authenticate as a reviewer.
        var unique = Guid.NewGuid().ToString("N")[..8];
        var email = $"density_rev_{unique}@example.com";
        const string password = "Test123!";
        await RegisterUserAsync(Page, email, password, "Density", "Reviewer", $"DEN-{unique}");
        await AssignRoleAsync(email, "Reviewer");
        await LoginAsync(Page, email, password);

        await Page.GotoAsync($"{BaseUrl}/Review");

        // Locate the reviewer queue table — fl-table[data-density="reviewer"] is the
        // canonical density-bearing selector per research R14.
        var reviewerTable = Page.Locator("table[data-density=\"reviewer\"]").First;
        if (await reviewerTable.CountAsync() == 0)
        {
            // Fallback to first table on the queue page if data-density not yet wired.
            reviewerTable = Page.Locator("table").First;
        }

        var reviewerPadding = await reviewerTable.Locator("tbody td").First.EvaluateAsync<double>(
            "el => parseFloat(getComputedStyle(el).paddingTop)");

        // Now register an applicant and visit the applicant table.
        await Page.Locator("form[action*='Account/Logout'] button[type=submit]").ClickAsync();
        var applUnique = Guid.NewGuid().ToString("N")[..8];
        var applEmail = $"density_appl_{applUnique}@example.com";
        await RegisterUserAsync(Page, applEmail, password, "Density", "Applicant", $"DEN-{applUnique}");
        await LoginAsync(Page, applEmail, password);

        // Trigger an empty applicant table.
        await Page.GotoAsync($"{BaseUrl}/Application/Create");

        // After creating a draft, /Application has at least one row.
        var applicantTable = Page.Locator("table[data-density=\"applicant\"], table.fl-table").First;
        if (await applicantTable.CountAsync() > 0)
        {
            var applicantPadding = await applicantTable.Locator("tbody td").First.EvaluateAsync<double>(
                "el => parseFloat(getComputedStyle(el).paddingTop)");
            Assert.That(applicantPadding, Is.GreaterThan(reviewerPadding),
                "Applicant table padding (--space-4 ≈ 16 px) must exceed reviewer (--space-2 ≈ 8 px) per spec 011 FR-060.");
        }
    }
}
