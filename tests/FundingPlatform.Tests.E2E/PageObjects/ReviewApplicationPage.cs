using FundingPlatform.Tests.E2E.Constants;
using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.PageObjects;

public class ReviewApplicationPage : BasePage
{
    public ReviewApplicationPage(IPage page) : base(page)
    {
    }

    public ILocator ApplicantName => Page.Locator(".applicant-name");
    public ILocator PerformanceScore => Page.Locator(".performance-score");
    public ILocator ApplicationState => Page.Locator(".application-state [data-testid=status-pill]");
    public ILocator ItemCards => Page.Locator(".review-item");
    public ILocator SendBackButton => Page.Locator($"button:has-text('{UiCopy.SendBack}')");
    public ILocator FinalizeButton => Page.Locator($"button:has-text('{UiCopy.FinalizeReview}')");
    public ILocator ForceFinalizationConfirm => Page.Locator("#forceFinalizationConfirm");
    public ILocator UnresolvedWarning => Page.Locator(".unresolved-warning");
    public ILocator SuccessMessage => Page.Locator(".alert-success");
    public ILocator ErrorMessage => Page.Locator(".alert-danger");

    public async Task GotoAsync(string baseUrl, int applicationId)
    {
        await Page.GotoAsync($"{baseUrl}/Review/{applicationId}");
    }

    private ILocator ItemCard(int itemId) =>
        Page.Locator($".review-item[data-item-id='{itemId}']");

    public ILocator ItemDecisionRadio(int itemId, string decision)
    {
        return ItemCard(itemId).Locator($"input[name='Decision'][value='{decision}']");
    }

    public ILocator ItemSupplierDropdown(int itemId)
    {
        return ItemCard(itemId).Locator("select[name='SelectedSupplierId']");
    }

    public ILocator ItemCommentField(int itemId)
    {
        return ItemCard(itemId).Locator("textarea[name='Comment']");
    }

    /// <summary>
    /// Spec 018 / FR-012 — reviewer-assigned LineCode input on the per-item
    /// decision form. The input is bound by data-testid+data-item-id; the
    /// existing Approve/Reject form posts both the LineCode and the Decision
    /// in a single round-trip.
    /// </summary>
    public ILocator ItemLineCodeInput(int itemId)
    {
        return Page.Locator($"[data-testid='review-item-line-code'][data-item-id='{itemId}']");
    }

    /// <summary>
    /// Spec 018 / T043 — convenience helper for legacy review tests that don't
    /// assert on the LineCode itself. Fills a deterministic <c>TEST-{itemId}</c>
    /// value (intentionally distinct from the <c>T1-N</c> production codes used
    /// in seed scenarios so debugging stays unambiguous).
    /// </summary>
    public async Task FillTestLineCodeAsync(int itemId)
    {
        var input = ItemLineCodeInput(itemId);
        if (await input.CountAsync() > 0)
        {
            await input.FillAsync($"TEST-{itemId}");
        }
    }

    public ILocator ItemSubmitButton(int itemId)
    {
        return Page.Locator($"button[data-item-id='{itemId}'].submit-decision");
    }

    /// <summary>
    /// Spec 018 / T043 — submits the per-item decision after threading a
    /// deterministic test LineCode through the form. Use this from legacy
    /// review-flow tests that don't otherwise assert on LineCode behaviour;
    /// tests that do exercise LineCode validation should fill the input
    /// directly via <see cref="ItemLineCodeInput(int)"/>.
    /// </summary>
    public async Task SubmitDecisionWithTestLineCodeAsync(int itemId)
    {
        await FillTestLineCodeAsync(itemId);
        await ItemSubmitButton(itemId).ClickAsync();
    }

    public ILocator ItemReviewStatusBadge(int itemId)
    {
        return ItemCard(itemId).Locator(".review-status-badge");
    }

    public ILocator TechnicalEquivalenceSubmit(int itemId)
    {
        return Page.Locator($"button[data-item-id='{itemId}'].submit-equivalence");
    }

    public ILocator RecommendedBadge(int itemId)
    {
        return ItemCard(itemId).Locator(".recommended-badge");
    }

    public ILocator QuotationRows(int itemId)
    {
        return ItemCard(itemId).Locator(".quotation-row");
    }

    public ILocator SupplierScores(int itemId)
    {
        return ItemCard(itemId).Locator(".supplier-score");
    }

    public ILocator ScoreBreakdowns(int itemId)
    {
        return ItemCard(itemId).Locator(".score-breakdown");
    }
}
