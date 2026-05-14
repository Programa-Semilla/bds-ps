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
    private readonly AppDbContext _db;
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
        var now = _clock.UtcNow;

        // Atomic single-use marker flip. EF Core 7+ ExecuteUpdateAsync emits a
        // single UPDATE … WHERE … so a racing consumer cannot double-spend the
        // token: the second UPDATE sees ConsumedAt already set and matches 0 rows.
        var affected = await _db.Set<PasswordResetToken>()
            .Where(t => t.UserId == userId
                     && t.TokenHash == tokenHash
                     && t.ConsumedAt == null
                     && t.ExpiresAt > now)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(t => t.ConsumedAt, _ => now),
                ct)
            .ConfigureAwait(false);

        if (affected == 0)
        {
            _logger.LogInformation(
                "Password reset token consume rejected for user {UserId} (expired, already consumed, or unknown).",
                userId);
            return false;
        }

        return true;
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
