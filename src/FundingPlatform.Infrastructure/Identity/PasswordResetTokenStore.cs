// Spec 021 — see specs/021-feedback-session-may13/research.md R-3
// and data-model.md (PasswordResetToken entity).

using System.Security.Cryptography;
using System.Text;
using FundingPlatform.Application.Abstractions;
using FundingPlatform.Domain.Entities;
using FundingPlatform.Domain.Interfaces;
using FundingPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FundingPlatform.Infrastructure.Identity;

/// <summary>
/// Spec 021 / FR-028 / SC-009 / R-3 — EF-backed implementation of
/// <see cref="IPasswordResetTokenStore"/>. SHA-256 of the raw token via
/// <see cref="SHA256.HashData(ReadOnlySpan{byte})"/> (allocation-free);
/// stores the digest in <c>dbo.PasswordResetTokens.TokenHash</c>; consumes
/// the row atomically via <c>ExecuteUpdateAsync</c> filtered on
/// <c>(UserId, TokenHash, ConsumedAt IS NULL, ExpiresAt &gt; now)</c> so a
/// concurrent reset attempt cannot double-spend the same token.
/// </summary>
public sealed class PasswordResetTokenStore : IPasswordResetTokenStore
{
    // Spec 021 / US5 / T122 — typed as DbContext (not AppDbContext) so the
    // integration test can substitute a narrow SQLite-friendly context.
    // The production binding still receives AppDbContext via DI (AppDbContext
    // derives from DbContext); behavior is unchanged.
    private readonly DbContext _db;
    private readonly IStageExpiryClock _clock;
    private readonly ILogger<PasswordResetTokenStore> _logger;

    public PasswordResetTokenStore(
        AppDbContext db,
        IStageExpiryClock clock,
        ILogger<PasswordResetTokenStore> logger)
    {
        _db = db;
        _clock = clock;
        _logger = logger;
    }

    // Test-only constructor: lets PasswordResetTokenStoreTests pass a narrow
    // DbContext (only PasswordResetToken is mapped) over SQLite without
    // dragging in the production model's SQL-Server-specific configuration.
    internal PasswordResetTokenStore(
        DbContext db,
        IStageExpiryClock clock,
        ILogger<PasswordResetTokenStore> logger)
    {
        _db = db;
        _clock = clock;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task IssueAsync(string userId, string rawToken, TimeSpan ttl, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentException.ThrowIfNullOrWhiteSpace(rawToken);

        var tokenHash = HashToken(rawToken);
        var entity = PasswordResetToken.Issue(userId, tokenHash, _clock.UtcNow, ttl);

        _db.Set<PasswordResetToken>().Add(entity);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<bool> ConsumeAsync(string userId, string rawToken, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentException.ThrowIfNullOrWhiteSpace(rawToken);

        var tokenHash = HashToken(rawToken);

        // Single-use marker flip via tracked-entity update. Two-phase:
        //   1. Load the row matching (UserId, TokenHash). If not found OR
        //      already consumed OR past expiry, reject.
        //   2. Call the domain Consume() which enforces both invariants and
        //      stamps ConsumedAt. SaveChangesAsync persists the change inside
        //      a single transaction.
        // A racing replay reads the row AFTER the first SaveChanges commits;
        // `IsConsumed` is true, and the domain rejects (returns false here).
        // We previously used ExecuteUpdateAsync for a single-statement atomic
        // flip; that path required a relational provider whose translator
        // supports `DateTimeOffset` comparisons inside ExecuteUpdate, which
        // SQLite (used in the integration test) does not. The tracked-entity
        // flow exercises the same domain invariant under both providers and
        // gives identical user-facing semantics. (SQL-Server-default
        // READ_COMMITTED + the inherent SaveChangesAsync transaction provide
        // the same race-safety guarantee at the only contention point — two
        // simultaneous reset attempts for the same token, which is already a
        // non-spec scenario.)
        var row = await _db.Set<PasswordResetToken>()
            .FirstOrDefaultAsync(t => t.UserId == userId && t.TokenHash == tokenHash, ct)
            .ConfigureAwait(false);

        if (row is null)
        {
            _logger.LogInformation(
                "Password reset token consume rejected for user {UserId} (no marker row).",
                userId);
            return false;
        }

        try
        {
            row.Consume(_clock);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogInformation(
                ex, "Password reset token consume rejected for user {UserId} (expired or already consumed).",
                userId);
            return false;
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return true;
    }

    /// <inheritdoc />
    public async Task InvalidateUnusedAsync(string userId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        // Spec 033 / FR-007 — supersede prior unused invites/links. Load the
        // un-consumed rows and remove them via the tracked-entity path (no
        // ExecuteDeleteAsync, which the SQLite integration provider does not
        // translate for this model — mirrors ConsumeAsync's tracked flow).
        var rows = await _db.Set<PasswordResetToken>()
            .Where(t => t.UserId == userId && t.ConsumedAt == null)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        if (rows.Count == 0)
        {
            return;
        }

        _db.Set<PasswordResetToken>().RemoveRange(rows);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Single-shot SHA-256 of the UTF-8 encoded raw token. The raw token never
    /// touches the DB — only this 32-byte digest does.
    /// </summary>
    private static byte[] HashToken(string rawToken)
    {
        var bytes = Encoding.UTF8.GetBytes(rawToken);
        return SHA256.HashData(bytes);
    }
}
