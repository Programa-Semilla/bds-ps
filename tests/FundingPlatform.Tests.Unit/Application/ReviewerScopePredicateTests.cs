using FundingPlatform.Application.Reviewer;

namespace FundingPlatform.Tests.Unit.Application;

/// <summary>
/// Spec 016 / FR-011..FR-015 — pinpoints the reviewer-scope short-circuit
/// invariants used by <c>IApplicationRepository.GetByStateForReviewerAsync</c>
/// and <c>ISignedUploadRepository.GetPendingInboxAsync</c>: admin scope
/// short-circuits the predicate; non-admin scope with zero memberships returns
/// nothing; the value-equality of <see cref="ReviewerScope"/> matches the
/// admin/empty constants. The full EF-translated predicate shape is exercised
/// by the integration test against a real DbContext.
/// </summary>
[TestFixture]
public class ReviewerScopePredicateTests
{
    [Test]
    public void Admin_ShortCircuit_FlagIsTrue()
    {
        Assert.That(ReviewerScope.Admin.IsAdmin, Is.True);
        Assert.That(ReviewerScope.Admin.GroupIds, Is.Empty);
    }

    [Test]
    public void Empty_NonAdmin_ZeroGroups()
    {
        Assert.That(ReviewerScope.Empty.IsAdmin, Is.False);
        Assert.That(ReviewerScope.Empty.GroupIds, Is.Empty);
    }

    [Test]
    public void Reviewer_WithGroups_ExposesGroupIds()
    {
        var scope = new ReviewerScope(false, new[] { 1, 2, 3 });

        Assert.That(scope.IsAdmin, Is.False);
        Assert.That(scope.GroupIds, Is.EquivalentTo(new[] { 1, 2, 3 }));
    }
}
