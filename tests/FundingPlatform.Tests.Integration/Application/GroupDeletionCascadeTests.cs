using FundingPlatform.Application.Admin.Groups;
using FundingPlatform.Application.Audit;
using FundingPlatform.Domain.Entities;
using FundingPlatform.Infrastructure.Audit;
using FundingPlatform.Infrastructure.Persistence;
using FundingPlatform.Infrastructure.Persistence.Configurations;
using FundingPlatform.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace FundingPlatform.Tests.Integration.Application;

/// <summary>
/// Spec 016 / Story 4 (FR-004, FR-005) — group deletion cascades cleanly.
/// SCOPE LIMITATION: EF InMemory provider for the cascade-via-service path
/// (the service explicitly removes the group + relies on EF cascade metadata
/// to also remove memberships). Real SQL ON DELETE CASCADE is exercised by
/// the E2E suite (T050) and by the cascade-shape assertion below (T051).
/// </summary>
[TestFixture]
public class GroupDeletionCascadeTests
{
    private const string ActorAdminId = "actor-admin-cascade";

    private static AppDbContext CreateContext(string dbName) =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .ConfigureWarnings(w => w.Ignore(
                Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    private static GroupService BuildService(AppDbContext ctx)
    {
        IAdminAuditWriter audit = new AdminAuditWriter(ctx);
        return new GroupService(ctx, audit);
    }

    private static async Task<ApplicationUser> SeedUserAsync(AppDbContext ctx, string email)
    {
        var user = new ApplicationUser(email, "F", "L", null) { Id = Guid.NewGuid().ToString() };
        ctx.Users.Add(user);
        await ctx.SaveChangesAsync();
        return user;
    }

    /// <summary>
    /// Story 4 acceptance scenario 1 — Norte has 3 members, Sur has 2 (one
    /// shared). Deleting Norte removes all 3 Norte memberships, leaves Sur
    /// memberships intact, and deletes zero user records.
    /// </summary>
    [Test]
    public async Task DeleteGroup_RemovesMemberships_PreservesOtherGroupAndUsers()
    {
        using var ctx = CreateContext($"cascade-1-{Guid.NewGuid():N}");
        var sut = BuildService(ctx);

        var norte = await sut.CreateAsync("Norte", ActorAdminId, CancellationToken.None);
        var sur = await sut.CreateAsync("Sur", ActorAdminId, CancellationToken.None);

        var u1 = await SeedUserAsync(ctx, "u1@example.com");
        var u2 = await SeedUserAsync(ctx, "u2@example.com");
        var u3 = await SeedUserAsync(ctx, "u3@example.com");
        var uShared = await SeedUserAsync(ctx, "shared@example.com");

        ctx.UserGroupMemberships.AddRange(
            new UserGroupMembership(u1.Id, norte),
            new UserGroupMembership(u2.Id, norte),
            new UserGroupMembership(u3.Id, norte),
            new UserGroupMembership(uShared.Id, norte),
            new UserGroupMembership(uShared.Id, sur));
        // Add one more Sur member.
        var uSur = await SeedUserAsync(ctx, "sur@example.com");
        ctx.UserGroupMemberships.Add(new UserGroupMembership(uSur.Id, sur));
        await ctx.SaveChangesAsync();

        var removed = await sut.DeleteAsync(norte, ActorAdminId, CancellationToken.None);

        Assert.That(removed, Is.EqualTo(4), "Norte had 4 members (u1, u2, u3, uShared).");
        var remaining = await ctx.UserGroupMemberships.ToListAsync();
        Assert.That(remaining.Select(m => m.GroupId).Distinct(), Is.EquivalentTo(new[] { sur }));
        Assert.That(remaining.Count(m => m.GroupId == sur), Is.EqualTo(2),
            "Sur still has 2 members (uShared + uSur).");

        var userCount = await ctx.Users.CountAsync();
        Assert.That(userCount, Is.GreaterThanOrEqualTo(5),
            "FR-005: no user records may be deleted by the cascade.");
    }

    /// <summary>
    /// T051 — explicit cascade-shape assertion. EF metadata MUST mark the
    /// UserGroupMembership → Group FK as Cascade so the dacpac and EF
    /// configurations agree on the rule. The dacpac file
    /// dbo.UserGroupMemberships.sql declares ON DELETE CASCADE; this test
    /// pins the C# side as well.
    /// </summary>
    [Test]
    public void EfMetadata_GroupForeignKey_IsCascade()
    {
        using var ctx = CreateContext($"cascade-meta-{Guid.NewGuid():N}");
        var entityType = ctx.Model.FindEntityType(typeof(UserGroupMembership))
            ?? throw new InvalidOperationException("UserGroupMembership entity type not in model.");
        var fkToGroup = entityType.GetForeignKeys()
            .Single(fk => fk.PrincipalEntityType.ClrType == typeof(Group));
        Assert.That(fkToGroup.DeleteBehavior, Is.EqualTo(DeleteBehavior.Cascade),
            "FR-004: deleting a Group MUST cascade through UserGroupMemberships.");

        var fkToUser = entityType.GetForeignKeys()
            .Single(fk => fk.PrincipalEntityType.ClrType == typeof(ApplicationUser));
        Assert.That(fkToUser.DeleteBehavior, Is.EqualTo(DeleteBehavior.Cascade),
            "Standard cascade behaviour for the user side.");
    }

    /// <summary>
    /// Story 4 acceptance scenario 2 — a reviewer left with zero memberships
    /// stays signed-in (i.e. the user record is intact and login is not
    /// blocked). The login path is HTTP-driven and lives in the E2E suite;
    /// here we assert the persistence preconditions: the user row survives
    /// and queue queries return empty for the reviewer.
    /// </summary>
    [Test]
    public async Task ReviewerWithOnlyDeletedGroup_RemainsLoggable_AndSeesEmptyQueue()
    {
        using var ctx = CreateContext($"cascade-2-{Guid.NewGuid():N}");
        var sut = BuildService(ctx);

        var norte = await sut.CreateAsync("Norte", ActorAdminId, CancellationToken.None);
        var rev = await SeedUserAsync(ctx, "rev@example.com");
        ctx.UserGroupMemberships.Add(new UserGroupMembership(rev.Id, norte));
        await ctx.SaveChangesAsync();

        await sut.DeleteAsync(norte, ActorAdminId, CancellationToken.None);

        // User row intact.
        var stillThere = await ctx.Users.AnyAsync(u => u.Id == rev.Id);
        Assert.That(stillThere, Is.True);

        // Reviewer scope is now empty.
        var memberships = await ctx.UserGroupMemberships
            .Where(m => m.UserId == rev.Id)
            .CountAsync();
        Assert.That(memberships, Is.EqualTo(0));
    }
}
