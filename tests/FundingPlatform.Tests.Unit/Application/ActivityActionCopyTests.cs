using FundingPlatform.Application.Services;
using NUnit.Framework;

namespace FundingPlatform.Tests.Unit.Application;

/// <summary>
/// es-CR contract for dashboard "Actividad reciente" titles. Every
/// VersionHistory action that the platform actually writes MUST map to a
/// Spanish string — never fall through to the raw English/internal action code
/// (e.g. "FlagEquivalence") on the applicant + reviewer timelines.
/// </summary>
[TestFixture]
public class ActivityActionCopyTests
{
    // The closed set of action codes written via `new VersionHistory(..., action, ...)`
    // across the codebase. Kept in sync with the grep of action literals.
    private static readonly string[] WrittenActions =
    {
        "Created", "Submitted", "StartReview", "ReviewItem",
        "SendBack", "Finalize", "FlagEquivalence", "Withdrawn",
        "AgreementGenerated", "AgreementRegenerated", "AgreementExecuted", "Funded",
    };

    [TestCaseSource(nameof(WrittenActions))]
    public void Title_MapsEveryWrittenAction_ToSpanish(string action)
    {
        var title = ActivityActionCopy.Title(action);

        Assert.That(title, Is.Not.Null.And.Not.Empty);
        Assert.That(title, Is.Not.EqualTo(action),
            $"Action '{action}' is not mapped — raw English/internal code would leak onto the timeline.");
    }

    [Test]
    public void Title_FlagEquivalence_And_Withdrawn_AreSpecificSpanish()
    {
        Assert.That(ActivityActionCopy.Title("FlagEquivalence"),
            Is.EqualTo("Equivalencia técnica actualizada"));
        Assert.That(ActivityActionCopy.Title("Withdrawn"),
            Is.EqualTo("Solicitud retirada"));
    }
}
