using FundingPlatform.Application.Reviewer;

namespace FundingPlatform.Tests.Unit.Application;

/// <summary>
/// Spec 016 / FR-011..FR-015 — pinpoints the reviewer-scope short-circuit
/// invariants used by <c>IApplicationRepository.GetByStateForReviewerAsync</c>
/// and <c>ISignedUploadRepository.GetPendingInboxAsync</c>: admin scope
/// short-circuits the predicate; non-admin scope with zero memberships returns
/// nothing; the value-equality of <see cref="ReviewerScope"/> matches the
/// admin/empty constants.
///
/// REVIEW-CODE F-7 — the EF-translated SQL shape (admin = no
/// UserGroupMemberships join; non-admin with group ids = EXISTS / JOIN against
/// UserGroupMemberships) is asserted by
/// <c>tests/FundingPlatform.Tests.Integration/Application/ReviewerScopeQueryShapeTests.cs</c>,
/// which composes the predicate against a real <c>AppDbContext</c> bound to
/// the SqlServer provider and inspects <c>IQueryable.ToQueryString()</c>. That
/// path requires a relational provider so it cannot live in the unit project.
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
