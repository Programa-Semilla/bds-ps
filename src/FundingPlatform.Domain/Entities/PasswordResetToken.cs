// Spec 021 — see specs/021-feedback-session-may13/data-model.md (PasswordResetToken)
// and research.md R-3 (single-use marker layered on top of Identity's token provider).

using FundingPlatform.Domain.Interfaces;

namespace FundingPlatform.Domain.Entities;

/// <summary>
/// Spec 021 / FR-028 / SC-009 — single-use marker row layered on top of ASP.NET
/// Identity's <c>DataProtectorTokenProvider</c>. Identity issues the cryptographic
/// token (60-minute TTL); this row hashes the dispatched token and records the
/// consumption timestamp so a replayed-within-TTL token is rejected.
///
/// The raw token is never persisted — only its SHA-256 hash. <see cref="Consume"/>
/// enforces the single-use + not-expired invariants in-domain; the controller
/// only sets the password after a successful Consume call.
/// </summary>
public class PasswordResetToken
{
    /// <summary>FR-028 — TTL per spec.</summary>
    public static readonly TimeSpan DefaultLifetime = TimeSpan.FromMinutes(60);

    public long Id { get; private set; }
    public string UserId { get; private set; } = string.Empty;
    public byte[] TokenHash { get; private set; } = [];
    public DateTimeOffset IssuedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? ConsumedAt { get; private set; }

    public bool IsConsumed => ConsumedAt is not null;

    private PasswordResetToken() { }

    private PasswordResetToken(string userId, byte[] tokenHash, DateTimeOffset issuedAt, DateTimeOffset expiresAt)
    {
        UserId = userId;
        TokenHash = tokenHash;
        IssuedAt = issuedAt;
        ExpiresAt = expiresAt;
    }

    /// <summary>
    /// Issues a new token row. <paramref name="tokenHash"/> is the SHA-256 digest
    /// of the raw token Identity dispatched to the user; the raw token never
    /// touches this table. <paramref name="ttl"/> defaults to
    /// <see cref="DefaultLifetime"/> (60 min) when null.
    /// </summary>
    public static PasswordResetToken Issue(
        string userId,
        byte[] tokenHash,
        DateTimeOffset now,
        TimeSpan? ttl = null)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ArgumentException("UserId is required.", nameof(userId));
        }
        ArgumentNullException.ThrowIfNull(tokenHash);
        if (tokenHash.Length == 0)
        {
            throw new ArgumentException("TokenHash must be non-empty.", nameof(tokenHash));
        }

        var lifetime = ttl ?? DefaultLifetime;
        if (lifetime <= TimeSpan.Zero)
        {
            throw new ArgumentException("TTL must be positive.", nameof(ttl));
        }

        return new PasswordResetToken(userId, tokenHash, now, now.Add(lifetime));
    }

    /// <summary>
    /// Marks the token as consumed. Throws if already consumed or if the current
    /// instant supplied by <paramref name="clock"/> is past <see cref="ExpiresAt"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Token is already consumed, or has expired.
    /// </exception>
    public void Consume(IStageExpiryClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        if (IsConsumed)
        {
            throw new InvalidOperationException("Password reset token has already been consumed.");
        }
        var now = clock.UtcNow;
        if (now >= ExpiresAt)
        {
            throw new InvalidOperationException("Password reset token has expired.");
        }
        ConsumedAt = now;
    }
}
