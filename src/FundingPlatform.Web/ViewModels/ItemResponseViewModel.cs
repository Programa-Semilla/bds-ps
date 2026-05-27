using FundingPlatform.Domain.Enums;

namespace FundingPlatform.Web.ViewModels;

public class ItemResponseViewModel
{
    public int ItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public ItemReviewStatus ReviewStatus { get; set; }
    public string? SelectedSupplierName { get; set; }
    public decimal? Amount { get; set; }
    public string? ReviewComment { get; set; }
    public ItemResponseDecision? Decision { get; set; }

    /// <summary>
    /// es-CR display copy for <see cref="Decision"/> (null when no decision yet).
    /// Built in the controller via <c>ItemResponseDecisionCopy.Label</c> so the
    /// view never reaches the enum's English <c>ToString()</c>.
    /// </summary>
    public string? DecisionLabel { get; set; }
}
