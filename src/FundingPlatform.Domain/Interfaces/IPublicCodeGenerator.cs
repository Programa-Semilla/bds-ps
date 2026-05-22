// Spec 021 — see specs/021-feedback-session-may13/research.md R-1.

using FundingPlatform.Domain.ValueObjects;

namespace FundingPlatform.Domain.Interfaces;

/// <summary>
/// Spec 021 / FR-008 — produces a fresh <see cref="PublicCode"/> guaranteed
/// unique against the <c>dbo.Applications.PublicCode</c> UNIQUE constraint.
///
/// Per <c>research.md</c> R-1: the implementation draws 5 crypto-RNG bytes,
/// encodes to 8 chars across the base32 alphabet <c>[A-HJ-NP-Z2-9]</c>,
/// inserts under the UNIQUE constraint, and retries up to 3 times on
/// <c>SqlException</c> 2627 (duplicate key). A 4th collision is logged + alerted
/// and surfaces as an exception — never as a user-visible failure.
/// </summary>
public interface IPublicCodeGenerator
{
    /// <summary>
    /// Generates a new <see cref="PublicCode"/> not currently present in the
    /// Applications table. May execute several DB round-trips on collision
    /// (≤ 3 retries per R-1).
    /// </summary>
    Task<PublicCode> GenerateAsync(CancellationToken ct = default);
}
