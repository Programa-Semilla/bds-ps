using FundingPlatform.Web.Helpers;
using NUnit.Framework;

namespace FundingPlatform.Tests.Unit.Web;

/// <summary>
/// Older rows persisted the English sentinel "Rejected: quotations are not
/// technically equivalent" in Item.ReviewComment (before FlagNotEquivalent
/// stopped writing it). The flag can also have been reset to false by
/// ResetReviewStatus while the comment was preserved. The applicant Details
/// page must still recognize that sentinel and render the localized message,
/// never the raw English.
/// </summary>
[TestFixture]
public class ReviewCommentDisplayTests
{
    [TestCase("Rejected: quotations are not technically equivalent")]
    [TestCase("rejected: quotations are not technically equivalent")]
    [TestCase("  Rejected: quotations are not technically equivalent  ")]
    public void IsLegacyNotEquivalentComment_RecognizesSentinel(string comment)
    {
        Assert.That(ReviewCommentDisplay.IsLegacyNotEquivalentComment(comment), Is.True);
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("Falta documentación de respaldo.")]
    [TestCase("Las cotizaciones no son técnicamente equivalentes.")]
    public void IsLegacyNotEquivalentComment_PassesThroughEverythingElse(string? comment)
    {
        Assert.That(ReviewCommentDisplay.IsLegacyNotEquivalentComment(comment), Is.False);
    }
}
