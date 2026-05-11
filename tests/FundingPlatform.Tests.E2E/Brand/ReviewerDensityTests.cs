using FundingPlatform.Tests.E2E.Fixtures;
using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.Brand;

/// <summary>
/// Spec 019 T046 / FR-019 / FR-031 — Reviewer surfaces use a tighter cell
/// padding than applicant surfaces. Spec 011 FR-060 canonical density rule
/// MUST NOT regress.
///
/// The rule lives entirely in tokens.css (.fl-table[data-density="reviewer"]
/// vs .fl-table[data-density="applicant"]), so this test measures the CSS
/// contract directly on a synthetic page. That avoids depending on a
/// reviewer queue being non-empty (spec 016: reviewers see only group-scoped
/// applications) or on applicant draft seed state.
/// </summary>
public class ReviewerDensityTests : AuthenticatedTestBase
{
    [Test]
    public async Task ReviewerTable_HasSmallerCellPadding_ThanApplicantTable()
    {
        await Page.SetContentAsync(@"
            <html>
              <head><link rel=""stylesheet"" href=""" + BaseUrl + @"/css/tokens.css"" /></head>
              <body>
                <table class=""fl-table"" data-density=""reviewer"" data-testid=""density-reviewer"">
                  <thead><tr><th>R</th></tr></thead>
                  <tbody><tr><td>r</td></tr></tbody>
                </table>
                <table class=""fl-table"" data-density=""applicant"" data-testid=""density-applicant"">
                  <thead><tr><th>A</th></tr></thead>
                  <tbody><tr><td>a</td></tr></tbody>
                </table>
              </body>
            </html>
        ");

        var reviewerCellPadding = await Page.Locator("[data-testid=\"density-reviewer\"] tbody td")
            .First.EvaluateAsync<double>("el => parseFloat(getComputedStyle(el).paddingTop)");
        var applicantCellPadding = await Page.Locator("[data-testid=\"density-applicant\"] tbody td")
            .First.EvaluateAsync<double>("el => parseFloat(getComputedStyle(el).paddingTop)");

        Assert.That(applicantCellPadding, Is.GreaterThan(reviewerCellPadding),
            $"Applicant table cell padding (--space-4 ≈ 16 px) must exceed reviewer (--space-2 ≈ 8 px) per spec 011 FR-060 / spec 019 FR-019. Measured: applicant={applicantCellPadding}, reviewer={reviewerCellPadding}.");
    }
}
