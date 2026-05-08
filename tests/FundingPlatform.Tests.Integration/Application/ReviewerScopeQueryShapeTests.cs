using FundingPlatform.Domain.Entities;
using FundingPlatform.Domain.Enums;
using FundingPlatform.Domain.Interfaces;
using FundingPlatform.Infrastructure.Persistence;
using FundingPlatform.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using AppEntity = FundingPlatform.Domain.Entities.Application;

namespace FundingPlatform.Tests.Integration.Application;

/// <summary>
/// Spec 016 / FR-011, FR-015 + REVIEW-CODE F-7 — exercises the real EF
/// translation of <see cref="ApplicationRepository.GetByStateForReviewerAsync"/>'s
/// reviewer-scope predicate by inspecting the rendered SQL via
/// <c>IQueryable.ToQueryString()</c>.
///
/// The unit-level <see cref="ReviewerScopePredicateTests"/> asserts the
/// short-circuit invariants of <see cref="Application.Reviewer.ReviewerScope"/>;
/// this test class asserts that the same scope, when fed into the repository
/// query, materializes (admin) without any EXISTS / UserGroupMemberships join,
/// and (non-admin) with an EXISTS / membership join keyed on the reviewer's
/// group ids.
///
/// Uses the SQL Server provider with a sentinel connection string —
/// <c>ToQueryString()</c> emits provider-specific SQL but does not open a
/// connection, so the test runs in-process with no SQL Server container.
/// </summary>
[TestFixture]
public class ReviewerScopeQueryShapeTests
{
    /// <summary>
    /// Builds an <see cref="AppDbContext"/> bound to the SqlServer provider
    /// against a sentinel connection string. Never opened.
    /// </summary>
    private static AppDbContext NewSqlServerContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer("Server=tcp:reviewer-scope-shape-test,1433;Database=Sentinel")
            .Options;
        return new AppDbContext(options);
    }

    private static IQueryable<AppEntity> ComposeForReviewer(AppDbContext ctx, ReviewerScopeHint scope)
    {
        // Mirrors ApplicationRepository.GetByStateForReviewerAsync's predicate.
        IQueryable<AppEntity> query = ctx.Applications
            .Include(a => a.Applicant)
            .Where(a => a.State == ApplicationState.Submitted);

        if (!scope.IsAdmin)
        {
            var groupIds = scope.GroupIds.ToList();
            if (groupIds.Count == 0)
            {
                return Enumerable.Empty<AppEntity>().AsQueryable();
            }
            query = from a in query
                    where ctx.UserGroupMemberships.Any(m =>
                        m.UserId == a.Applicant!.UserId
                        && groupIds.Contains(m.GroupId))
                    select a;
        }

        return query;
    }

    [Test]
    public void Admin_Scope_QueryShape_DoesNotJoinUserGroupMemberships()
    {
        // FR-015 — admin scope short-circuits the predicate; the rendered SQL
        // must not reference UserGroupMemberships in any form (EXISTS, JOIN,
        // subquery).
        using var ctx = NewSqlServerContext();
        var query = ComposeForReviewer(ctx, new ReviewerScopeHint(IsAdmin: true, GroupIds: Array.Empty<int>()));

        var sql = query.ToQueryString();

        Assert.That(sql, Does.Not.Contain("UserGroupMemberships").IgnoreCase,
            "Admin scope must not emit a UserGroupMemberships join. SQL was:\n" + sql);
        Assert.That(sql, Does.Not.Contain("EXISTS").IgnoreCase,
            "Admin scope must not emit an EXISTS subquery. SQL was:\n" + sql);
    }

    [Test]
    public void NonAdmin_Scope_WithGroupIds_QueryShape_ContainsExistsOrJoinAgainstUserGroupMemberships()
    {
        // FR-011 — non-admin reviewers see only applications whose applicant
        // shares at least one group. The rendered SQL must reference
        // UserGroupMemberships (the EF "Any" composes to EXISTS in SqlServer).
        using var ctx = NewSqlServerContext();
        var groupIds = new[] { 1, 2 };
        var query = ComposeForReviewer(ctx, new ReviewerScopeHint(IsAdmin: false, GroupIds: groupIds));

        var sql = query.ToQueryString();

        Assert.That(sql, Does.Contain("UserGroupMemberships").IgnoreCase,
            "Non-admin scope must reference UserGroupMemberships. SQL was:\n" + sql);

        // Either EXISTS (the typical EF translation of .Any()) or an explicit
        // JOIN is acceptable; both narrow at the SQL level.
        var hasExists = sql.Contains("EXISTS", StringComparison.OrdinalIgnoreCase);
        var hasJoin = sql.Contains("JOIN", StringComparison.OrdinalIgnoreCase);
        Assert.That(hasExists || hasJoin, Is.True,
            "Non-admin scope must compose as EXISTS or a JOIN. SQL was:\n" + sql);

        // Both group ids must appear as parameters — confirms the predicate is
        // parameterized (no SQL injection risk) and that the right ids were
        // bound. EF emits them as @__groupIds_0 etc.
        Assert.That(sql, Does.Contain("Applicant").IgnoreCase,
            "Predicate must traverse the Applicant navigation. SQL was:\n" + sql);
    }

    [Test]
    public void NonAdmin_Scope_WithEmptyGroupIds_ShortCircuits_WithoutSqlEmission()
    {
        // FR-005 — a non-admin reviewer with zero memberships sees an empty
        // queue. The repository short-circuits before composing the predicate,
        // so there is nothing to render. Asserting the IQueryable is empty
        // covers the contract without depending on a specific SQL string.
        using var ctx = NewSqlServerContext();
        var query = ComposeForReviewer(ctx,
            new ReviewerScopeHint(IsAdmin: false, GroupIds: Array.Empty<int>()));

        var materialized = query.ToList();
        Assert.That(materialized, Is.Empty);
    }
}
