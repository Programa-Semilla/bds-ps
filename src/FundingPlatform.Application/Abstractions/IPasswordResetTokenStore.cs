// Spec 021 — see specs/021-feedback-session-may13/research.md R-3.

namespace FundingPlatform.Application.Abstractions;

/// <summary>
/// Spec 021 / FR-028 / SC-009 — narrow seam over the
/// <c>dbo.PasswordResetTokens</c> single-use marker table layered on top of
/// ASP.NET Identity's <c>DataProtectorTokenProvider</c>. Identity issues the
/// cryptographic token (60-minute TTL); the store records a SHA-256 hash of
/// the dispatched token and atomically marks it consumed on first reset so a
/// within-TTL replay is rejected.
///
/// Hashing happens inside the implementation — callers pass the raw token
/// straight through. The store never persists the raw token.
/// </summary>
public interface IPasswordResetTokenStore
{
    /// <summary>
    /// Persists a fresh single-use marker row for <paramref name="userId"/> + the
    /// SHA-256 hash of <paramref name="rawToken"/>. <paramref name="ttl"/> stamps
    /// <c>ExpiresAt = now + ttl</c> (the spec default is 60 min, matching the
    /// Identity provider's <c>TokenLifespan</c>).
    /// </summary>
    Task IssueAsync(string userId, string rawToken, TimeSpan ttl, CancellationToken ct);

    /// <summary>
    /// Atomically consumes the marker row matching <paramref name="userId"/> +
    /// SHA-256(<paramref name="rawToken"/>) IF it is not yet consumed and not
    /// expired. Returns <c>true</c> on successful consume (controller may then
    /// reset the password); returns <c>false</c> if no eligible row exists —
    /// the caller MUST treat this as a rejected reset (expired/already-used/
    /// fabricated token).
    /// </summary>
    Task<bool> ConsumeAsync(string userId, string rawToken, CancellationToken ct);

    /// <summary>
    /// Spec 033 / FR-007 — deletes every un-consumed marker row for
    /// <paramref name="userId"/> (DELETE WHERE <c>UserId=@id AND ConsumedAt IS NULL</c>).
    /// Called before issuing a fresh invitation so a resent invite supersedes
    /// any prior unused link (the older link then fails to consume). Consumed
    /// rows are left intact as the single-use audit trail.
    /// </summary>
    Task InvalidateUnusedAsync(string userId, CancellationToken ct);
}
