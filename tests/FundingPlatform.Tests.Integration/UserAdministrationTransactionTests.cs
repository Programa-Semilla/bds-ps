using FundingPlatform.Application.Admin.Users.DTOs;
using FundingPlatform.Application.Audit;
using FundingPlatform.Domain.Entities;
using FundingPlatform.Infrastructure.Audit;
using FundingPlatform.Infrastructure.Identity;
using FundingPlatform.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;

namespace FundingPlatform.Tests.Integration;

/// <summary>
/// Spec 016 / REVIEW-CODE F-3 — admin user-edit must wrap the membership-diff
/// + user-save sequence in a single relational transaction so a failure on the
/// later SaveChanges does not leave a half-applied edit.
///
/// The rest of the integration tests use EF InMemory (which silently ignores
/// transactions). To exercise the real Begin/Rollback path this test class uses
/// the SQLite in-memory provider — a real relational provider with transaction
/// semantics that runs in-process, no SQL Server container needed.
/// </summary>
[TestFixture]
public class UserAdministrationTransactionTests
{
    private const string ActorAdminId = "actor-admin-tx";

    /// <summary>
    /// SaveChanges interceptor that throws on the SaveChanges call that
    /// inserts at least one <c>UserGroupMembership</c> entity — i.e. the
    /// membership-diff save inside <c>UpdateUserAsync</c>. Earlier saves
    /// (UserManager.UpdateAsync, role swap, security stamp, applicant upsert)
    /// pass through untouched.
    /// </summary>
    /// <summary>
    /// SQLite-friendly variant of <see cref="AppDbContext"/>. Strips SqlServer-
    /// specific column metadata (collation, <c>SYSUTCDATETIME()</c> default-value
    /// SQL) added by entity configurations so <c>EnsureCreated</c> can issue
    /// SQLite-compatible CREATE TABLE statements. Domain behaviour is unchanged
    /// — what matters here is that the relational provider supports
    /// <c>BeginTransactionAsync</c>/<c>RollbackAsync</c> for the F-3 fix.
    /// </summary>
    private sealed class SqliteAppDbContext : AppDbContext
    {
        public SqliteAppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            foreach (var entity in modelBuilder.Model.GetEntityTypes())
            {
                foreach (var prop in entity.GetProperties())
                {
                    // SqlServer collation — SQLite has no equivalent.
                    prop.SetCollation(null);
                    // SYSUTCDATETIME() default — replace with CURRENT_TIMESTAMP
                    // for SQLite so EnsureCreated produces a valid table.
                    var defaultSql = prop.GetDefaultValueSql();
                    if (!string.IsNullOrEmpty(defaultSql)
                        && defaultSql.Contains("SYSUTCDATETIME", StringComparison.OrdinalIgnoreCase))
                    {
                        prop.SetDefaultValueSql("CURRENT_TIMESTAMP");
                    }
                }
            }
        }
    }

    private sealed class FailOnMembershipSaveInterceptor : SaveChangesInterceptor
    {
        public bool Triggered { get; private set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            var ctx = eventData.Context;
            if (ctx is not null
                && ctx.ChangeTracker.Entries<UserGroupMembership>()
                    .Any(e => e.State == EntityState.Added || e.State == EntityState.Deleted))
            {
                Triggered = true;
                throw new InvalidOperationException(
                    "Simulated fault on membership SaveChanges (REVIEW-CODE F-3 transaction test).");
            }
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    private static (UserAdministrationService service, AppDbContext ctx, IServiceProvider sp,
                    SqliteConnection conn, FailOnMembershipSaveInterceptor interceptor) Build(
        bool injectFault)
    {
        // Open a single SQLite connection for the lifetime of the context so
        // the in-memory database survives between commands.
        var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();

        var interceptor = new FailOnMembershipSaveInterceptor();
        var sp = BuildServiceProvider(conn, injectFault ? interceptor : null);
        var ctx = sp.GetRequiredService<AppDbContext>();
        ctx.Database.EnsureCreated();

        var sut = sp.GetRequiredService<UserAdministrationService>();
        return (sut, ctx, sp, conn, interceptor);
    }

    private static IServiceProvider BuildServiceProvider(
        SqliteConnection conn,
        FailOnMembershipSaveInterceptor? faultInterceptor)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        // Register AppDbContext as the public service type, but instantiate
        // SqliteAppDbContext so the SqlServer-specific column metadata is
        // stripped before EnsureCreated runs against SQLite. We avoid
        // AddDbContext<TService,TImpl> because it doesn't make
        // DbContextOptions<AppDbContext> resolvable for the impl ctor.
        services.AddSingleton<DbContextOptions<AppDbContext>>(_ =>
        {
            var b = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(conn);
            if (faultInterceptor is not null)
            {
                b.AddInterceptors(faultInterceptor);
            }
            return b.Options;
        });
        services.AddScoped<AppDbContext>(sp =>
            new SqliteAppDbContext(sp.GetRequiredService<DbContextOptions<AppDbContext>>()));
        services.AddIdentity<ApplicationUser, IdentityRole>(o =>
            {
                o.Password.RequireDigit = false;
                o.Password.RequireLowercase = false;
                o.Password.RequireUppercase = false;
                o.Password.RequireNonAlphanumeric = false;
                o.Password.RequiredLength = 4;
            })
            .AddEntityFrameworkStores<AppDbContext>();
        services.AddScoped<IAdminAuditWriter, AdminAuditWriter>();
        services.AddScoped<UserAdministrationService>();
        services.AddHttpContextAccessor();
        return services.BuildServiceProvider();
    }

    private static async Task SeedRolesAsync(IServiceProvider sp)
    {
        var roleMgr = sp.GetRequiredService<RoleManager<IdentityRole>>();
        foreach (var r in new[] { "Applicant", "Reviewer", "Admin" })
        {
            if (!await roleMgr.RoleExistsAsync(r))
            {
                await roleMgr.CreateAsync(new IdentityRole(r));
            }
        }
    }

    private static async Task<int[]> SeedGroupsAsync(AppDbContext ctx, params string[] names)
    {
        foreach (var n in names)
        {
            ctx.Groups.Add(Group.Create(n));
        }
        await ctx.SaveChangesAsync();
        return ctx.Groups.OrderBy(g => g.Id).Select(g => g.Id).ToArray();
    }

    [Test]
    public async Task UpdateUser_FailureOnMembershipSave_RollsBackUserRowChanges()
    {
        // Arrange — seed a Reviewer with one group, then trigger an update that
        // (a) renames the user and (b) adds a second group. The interceptor
        // throws on the membership SaveChanges. Without the explicit
        // transaction wrapper added in REVIEW-CODE F-3 the user's first/last
        // name would be persisted while the membership change is rolled back.
        // With the wrapper, both changes are rolled back together.
        var (createSut, createCtx, createSp, createConn, _) = Build(injectFault: false);
        try
        {
            await SeedRolesAsync(createSp);
            var ids = await SeedGroupsAsync(createCtx, "Norte", "Sur");
            var (norte, sur) = (ids[0], ids[1]);

            var created = await createSut.CreateUserAsync(
                new CreateUserRequest("Original", "Name", "tx@test.com", null, "Reviewer",
                    "Test1!", null, GroupIds: new[] { norte }),
                ActorAdminId, CancellationToken.None);
            Assert.That(created.Succeeded, Is.True,
                string.Join("; ", created.Errors.Select(e => e.Message)));
            var userId = created.Value!.Id;
            var concurrencyStampBefore = created.Value!.ConcurrencyStamp;

            // Cross-context check: the seeded user is reachable via the same
            // SQLite connection from a fresh context.
            await using (var verifyCtx = new SqliteAppDbContext(
                new DbContextOptionsBuilder<AppDbContext>().UseSqlite(createConn).Options))
            {
                var seeded = await verifyCtx.Users.FirstOrDefaultAsync(u => u.Id == userId);
                Assert.That(seeded, Is.Not.Null);
                Assert.That(seeded!.FirstName, Is.EqualTo("Original"));
            }

            // Build a *second* SUT against the SAME SQLite connection so we
            // share the underlying database, and inject the fault interceptor.
            var faultInterceptor = new FailOnMembershipSaveInterceptor();
            var faultSp = BuildServiceProvider(createConn, faultInterceptor);
            using var scope = faultSp.CreateScope();
            var faultSut = scope.ServiceProvider.GetRequiredService<UserAdministrationService>();

            // Act — attempt the update. The membership add will fault.
            Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await faultSut.UpdateUserAsync(
                    new UpdateUserRequest(userId, "Renamed", "Person", "tx@test.com", null,
                        "Reviewer", null, GroupIds: new[] { norte, sur },
                        ConcurrencyStamp: concurrencyStampBefore),
                    ActorAdminId, CancellationToken.None));

            Assert.That(faultInterceptor.Triggered, Is.True,
                "Interceptor must have fired on the membership-diff SaveChanges.");

            // Assert — user-row updates are rolled back.
            await using var checkCtx = new SqliteAppDbContext(
                new DbContextOptionsBuilder<AppDbContext>().UseSqlite(createConn).Options);
            var afterUser = await checkCtx.Users.FirstOrDefaultAsync(u => u.Id == userId);
            Assert.That(afterUser, Is.Not.Null);
            Assert.That(afterUser!.FirstName, Is.EqualTo("Original"),
                "FR-008 / REVIEW-CODE F-3: user-row update must be rolled back when membership save fails.");
            Assert.That(afterUser.LastName, Is.EqualTo("Name"),
                "REVIEW-CODE F-3: user-row update must be rolled back when membership save fails.");

            var memberships = await checkCtx.UserGroupMemberships
                .Where(m => m.UserId == userId)
                .Select(m => m.GroupId)
                .ToListAsync();
            Assert.That(memberships, Is.EquivalentTo(new[] { norte }),
                "Membership rows must reflect the pre-update state after rollback.");
        }
        finally
        {
            createConn.Close();
            createConn.Dispose();
        }
    }

    [Test]
    public async Task UpdateUser_HappyPath_OnRelationalProvider_CommitsAllChanges()
    {
        // Sanity check on the same SQLite stack: with no fault injection, the
        // explicit transaction commits successfully and both user-row updates
        // and membership-diff persist.
        var (sut, ctx, sp, conn, _) = Build(injectFault: false);
        try
        {
            await SeedRolesAsync(sp);
            var ids = await SeedGroupsAsync(ctx, "Norte", "Sur");
            var (norte, sur) = (ids[0], ids[1]);

            var created = await sut.CreateUserAsync(
                new CreateUserRequest("F", "L", "happy-tx@test.com", null, "Reviewer",
                    "Test1!", null, GroupIds: new[] { norte }),
                ActorAdminId, CancellationToken.None);
            Assert.That(created.Succeeded, Is.True);
            var userId = created.Value!.Id;

            var fresh = await sut.GetUserAsync(userId, CancellationToken.None);
            var update = await sut.UpdateUserAsync(
                new UpdateUserRequest(userId, "Updated", "Name", "happy-tx@test.com", null,
                    "Reviewer", null, GroupIds: new[] { norte, sur },
                    ConcurrencyStamp: fresh!.ConcurrencyStamp),
                ActorAdminId, CancellationToken.None);

            Assert.That(update.Succeeded, Is.True,
                string.Join("; ", update.Errors.Select(e => e.Message)));

            await using var checkCtx = new SqliteAppDbContext(
                new DbContextOptionsBuilder<AppDbContext>().UseSqlite(conn).Options);
            var afterUser = await checkCtx.Users.FirstOrDefaultAsync(u => u.Id == userId);
            Assert.That(afterUser!.FirstName, Is.EqualTo("Updated"));
            var memberships = await checkCtx.UserGroupMemberships
                .Where(m => m.UserId == userId)
                .Select(m => m.GroupId)
                .OrderBy(x => x)
                .ToListAsync();
            Assert.That(memberships, Is.EquivalentTo(new[] { norte, sur }));
        }
        finally
        {
            conn.Close();
            conn.Dispose();
        }
    }
}
