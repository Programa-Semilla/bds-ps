namespace FundingPlatform.Web.Helpers;

/// <summary>
/// Recognizes the legacy English sentinel that older rows persisted in
/// <c>Item.ReviewComment</c> when an item was flagged "not technically
/// equivalent". <c>Item.FlagNotEquivalent</c> no longer writes it (the
/// <c>IsNotTechnicallyEquivalent</c> boolean carries the state), but pre-fix
/// data — and rows whose flag was later cleared by <c>ResetReviewStatus</c>
/// while the comment was preserved — can still hold it. The applicant Details
/// page treats a match as the not-equivalent case and renders the es-CR message
/// instead of the raw English string.
/// </summary>
public static class ReviewCommentDisplay
{
    private const string LegacyNotEquivalentSentinel =
        "Rejected: quotations are not technically equivalent";

    public static bool IsLegacyNotEquivalentComment(string? comment) =>
        !string.IsNullOrWhiteSpace(comment)
        && string.Equals(comment.Trim(), LegacyNotEquivalentSentinel, StringComparison.OrdinalIgnoreCase);
}
