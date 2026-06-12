// Spec 021 / US5 / T122 / FR-028 / R-3 — integration coverage for
// PasswordResetTokenStore (the single-use marker layered on top of ASP.NET
// Identity's DataProtectorTokenProvider). EF in-memory keeps the test
// hermetic (no SQL Server); the implementation's ExecuteUpdateAsync atomic
// flip is exercised against the InMemory provider exactly the way it runs
// in production.

using FundingPlatform.Application.Abstractions;
using FundingPlatform.Domain.Entities;
using FundingPlatform.Domain.Interfaces;
using FundingPlatform.Infrastructure.Identity;
using FundingPlatform.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace FundingPlatform.Tests.Integration.Identity;

[TestFixture]
public class PasswordResetTokenStoreTests
{
    // SQLite (in-memory shared connection) — chosen over EF InMemory because
    // PasswordResetTokenStore.ConsumeAsync uses ExecuteUpdateAsync, which the
    // EF InMemory provider does not support. SQLite supports it and gives us
    // a real atomic UPDATE … WHERE … against a SQL-flavored backing store.
    //
    // We bind a *narrow* DbContext (PasswordResetTokensOnlyContext, below)
    // because the production AppDbContext registers SQL-Server-specific
    // configuration (Latin1_General_CI_AI collations, VARBINARY(64) column
    // types, computed columns) that SQLite cannot parse during
    // EnsureCreated. The store under test exercises the exact production
    // ExecuteUpdateAsync code path via its DbContext-typed internal ctor.
    private SqliteConnection _connection = null!;
    private PasswordResetTokensOnlyContext _ctx = null!;
    private FakeClock _clock = null!;
    private IPasswordResetTokenStore _store = null!;
    private DateTimeOffset _now;

    [SetUp]
    public void Setup()
    {
        _now = new DateTimeOffset(2026, 5, 14, 12, 0, 0, TimeSpan.Zero);
        _clock = new FakeClock(_now);

        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<PasswordResetTokensOnlyContext>()
            .UseSqlite(_connection)
            .Options;
        _ctx = new PasswordResetTokensOnlyContext(options);
        _ctx.Database.EnsureCreated();
        // Test-only ctor on PasswordResetTokenStore accepts any DbContext.
        _store = new PasswordResetTokenStore(
            (DbContext)_ctx, _clock, NullLogger<PasswordResetTokenStore>.Instance);
    }

    [TearDown]
    public void TearDown()
    {
        _ctx.Dispose();
        _connection.Dispose();
    }

    [Test]
    public async Task IssueAsync_PersistsHashedToken()
    {
        const string userId = "user-1";
        const string rawToken = "raw-token-abc";

        await _store.IssueAsync(userId, rawToken, TimeSpan.FromMinutes(60), CancellationToken.None);

        var rows = await _ctx.Set<PasswordResetToken>().ToListAsync();
        Assert.That(rows, Has.Count.EqualTo(1));
        var row = rows[0];
        Assert.Multiple(() =>
        {
            Assert.That(row.UserId, Is.EqualTo(userId));
            Assert.That(row.TokenHash, Has.Length.EqualTo(32), "SHA-256 digest is 32 bytes");
            Assert.That(row.TokenHash.SequenceEqual(System.Text.Encoding.UTF8.GetBytes(rawToken)), Is.False,
                "raw token MUST NOT be persisted; only its hash");
            Assert.That(row.IssuedAt, Is.EqualTo(_now));
            Assert.That(row.ExpiresAt, Is.EqualTo(_now.AddMinutes(60)));
            Assert.That(row.ConsumedAt, Is.Null);
        });
    }

    [Test]
    public async Task ConsumeAsync_ValidToken_Succeeds_AndMarksConsumed()
    {
        const string userId = "user-1";
        const string rawToken = "raw-token-abc";

        await _store.IssueAsync(userId, rawToken, TimeSpan.FromMinutes(60), CancellationToken.None);

        // Move clock forward 5 min — well inside the TTL.
        _clock.UtcNow = _now.AddMinutes(5);

        var consumed = await _store.ConsumeAsync(userId, rawToken, CancellationToken.None);
        Assert.That(consumed, Is.True);

        var row = await _ctx.Set<PasswordResetToken>().AsNoTracking().FirstAsync();
        Assert.That(row.ConsumedAt, Is.EqualTo(_now.AddMinutes(5)),
            "ConsumedAt MUST be stamped with the clock's current instant");
    }

    [Test]
    public async Task ConsumeAsync_SameTokenTwice_SecondAttemptReturnsFalse()
    {
        const string userId = "user-1";
        const string rawToken = "raw-token-abc";

        await _store.IssueAsync(userId, rawToken, TimeSpan.FromMinutes(60), CancellationToken.None);

        _clock.UtcNow = _now.AddMinutes(5);
        var first = await _store.ConsumeAsync(userId, rawToken, CancellationToken.None);
        Assert.That(first, Is.True);

        _clock.UtcNow = _now.AddMinutes(10);
        var second = await _store.ConsumeAsync(userId, rawToken, CancellationToken.None);

        Assert.That(second, Is.False, "Single-use: a replay within the TTL MUST be rejected");
    }

    [Test]
    public async Task ConsumeAsync_ExpiredToken_ReturnsFalse()
    {
        const string userId = "user-1";
        const string rawToken = "raw-token-abc";

        await _store.IssueAsync(userId, rawToken, TimeSpan.FromMinutes(60), CancellationToken.None);

        // 61 minutes later — past the 60-minute TTL.
        _clock.UtcNow = _now.AddMinutes(61);

        var consumed = await _store.ConsumeAsync(userId, rawToken, CancellationToken.None);

        Assert.That(consumed, Is.False, "Expired tokens MUST be rejected");
        var row = await _ctx.Set<PasswordResetToken>().AsNoTracking().FirstAsync();
        Assert.That(row.ConsumedAt, Is.Null,
            "Expired token row MUST NOT be flipped to consumed by a rejected consume");
    }

    [Test]
    public async Task ConsumeAsync_WrongRawToken_ReturnsFalse()
    {
        const string userId = "user-1";

        await _store.IssueAsync(userId, "the-issued-token", TimeSpan.FromMinutes(60), CancellationToken.None);

        var consumed = await _store.ConsumeAsync(userId, "a-different-token", CancellationToken.None);

        Assert.That(consumed, Is.False, "Hash mismatch MUST reject the consume");
    }

    // -----------------------------------------------------------------------
    // Spec 033 — 72h invite TTL + supersede-on-resend (InvalidateUnusedAsync).
    // -----------------------------------------------------------------------

    [Test]
    public async Task IssueAsync_With72hTtl_PersistsExpiresAtPlus72h()
    {
        // Spec 033 / FR-006 — an invite is just a row with a 72h ExpiresAt.
        await _store.IssueAsync("user-1", "invite-token", PasswordResetToken.InvitationLifetime, CancellationToken.None);

        var row = await _ctx.Set<PasswordResetToken>().AsNoTracking().FirstAsync();
        Assert.That(row.ExpiresAt, Is.EqualTo(_now.AddHours(72)));
    }

    [Test]
    public async Task InvalidateUnusedAsync_DeletesUnconsumedRows_LeavesConsumed()
    {
        const string userId = "user-1";

        // One token that we consume, then a second still-unused token.
        await _store.IssueAsync(userId, "first-token", PasswordResetToken.InvitationLifetime, CancellationToken.None);
        _clock.UtcNow = _now.AddMinutes(5);
        Assert.That(await _store.ConsumeAsync(userId, "first-token", CancellationToken.None), Is.True);
        await _store.IssueAsync(userId, "second-token", PasswordResetToken.InvitationLifetime, CancellationToken.None);

        // A different user's unused token must survive.
        await _store.IssueAsync("other-user", "other-token", PasswordResetToken.InvitationLifetime, CancellationToken.None);

        await _store.InvalidateUnusedAsync(userId, CancellationToken.None);

        var remaining = await _ctx.Set<PasswordResetToken>().AsNoTracking().ToListAsync();
        // The consumed first-token row (audit trail) and the other user's row survive;
        // the user's unused second-token row is deleted.
        Assert.That(remaining.Count(r => r.UserId == userId), Is.EqualTo(1));
        Assert.That(remaining.Single(r => r.UserId == userId).ConsumedAt, Is.Not.Null,
            "Only the consumed row should remain for the user.");
        Assert.That(remaining.Any(r => r.UserId == "other-user"), Is.True,
            "Another user's unused token MUST NOT be touched.");
    }

    [Test]
    public async Task InvalidateUnusedAsync_ThenConsumePriorToken_ReturnsFalse()
    {
        const string userId = "user-1";

        await _store.IssueAsync(userId, "stale-token", PasswordResetToken.InvitationLifetime, CancellationToken.None);

        // Resend supersedes: invalidate the prior unused token before issuing afresh.
        await _store.InvalidateUnusedAsync(userId, CancellationToken.None);
        await _store.IssueAsync(userId, "fresh-token", PasswordResetToken.InvitationLifetime, CancellationToken.None);

        _clock.UtcNow = _now.AddMinutes(5);
        Assert.That(await _store.ConsumeAsync(userId, "stale-token", CancellationToken.None), Is.False,
            "The superseded link MUST be rejected.");
        Assert.That(await _store.ConsumeAsync(userId, "fresh-token", CancellationToken.None), Is.True,
            "The freshly issued link MUST still work.");
    }

    private sealed class FakeClock : IStageExpiryClock
    {
        public FakeClock(DateTimeOffset now) { UtcNow = now; }
        public DateTimeOffset UtcNow { get; set; }
    }

    /// <summary>
    /// Narrow DbContext used to create + assert the
    /// <c>PasswordResetTokens</c> table over SQLite without dragging in the
    /// production model's SQL-Server-only column types and collations.
    /// </summary>
    internal sealed class PasswordResetTokensOnlyContext : DbContext
    {
        public PasswordResetTokensOnlyContext(DbContextOptions<PasswordResetTokensOnlyContext> options)
            : base(options) { }

        public DbSet<PasswordResetToken> PasswordResetTokens => Set<PasswordResetToken>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<PasswordResetToken>(b =>
            {
                b.ToTable("PasswordResetTokens");
                b.HasKey(t => t.Id);
                b.Property(t => t.Id).ValueGeneratedOnAdd();
                b.Property(t => t.UserId).IsRequired();
                b.Property(t => t.TokenHash).IsRequired();
                b.Property(t => t.IssuedAt).IsRequired();
                b.Property(t => t.ExpiresAt).IsRequired();
                b.Property(t => t.ConsumedAt);
            });
        }
    }
}
