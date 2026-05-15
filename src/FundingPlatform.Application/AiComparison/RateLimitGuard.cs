using FundingPlatform.Application.Abstractions.AiComparison;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace FundingPlatform.Application.AiComparison;

/// <summary>
/// Spec 020 / FR-G1 — per-application 24h rate limit. Implemented as a counter
/// over the existing AdminAuditEvent table (no schema change). The orchestrator
/// invokes <see cref="EnforceAsync"/> after the cache-hit short-circuit and
/// before any provider call.
///
/// US1 ships with the predicate wired but the implementation is a no-op shim
/// until US4 fleshes out the audit-row count + admin bypass branch. Marked
/// virtual so US4 can land the real predicate without changing callers.
/// </summary>
public class RateLimitGuard
{
    private readonly IRateLimitCounter _counter;
    private readonly IConfiguration _configuration;
    private readonly ILogger<RateLimitGuard> _logger;

    public RateLimitGuard(
        IRateLimitCounter counter,
        IConfiguration configuration,
        ILogger<RateLimitGuard> logger)
    {
        _counter = counter;
        _configuration = configuration;
        _logger = logger;
    }

    public virtual async Task EnforceAsync(
        int applicationId,
        string actorRole,
        bool bypassRateLimit,
        CancellationToken ct)
    {
        if (bypassRateLimit && string.Equals(actorRole, "Admin", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation("RateLimitGuard bypassed by admin for application {ApplicationId}.", applicationId);
            return;
        }

        var cap = int.TryParse(_configuration["AiComparison:RateLimitPerApp24h"], out var c) ? c : 10;
        var windowStart = DateTimeOffset.UtcNow.AddHours(-24);
        var count = await _counter.CountAttemptsAsync(applicationId, windowStart, ct);

        if (count >= cap)
        {
            var resets = windowStart.AddHours(24);
            throw new RateLimitExceededException(remaining: 0, windowResetsAt: resets);
        }
    }
}

/// <summary>
/// Spec 020 — abstracts the audit-row count behind a tiny interface so the
/// guard can be unit-tested without spinning EF. The Infrastructure project
/// supplies the EF-backed implementation.
/// </summary>
public interface IRateLimitCounter
{
    Task<int> CountAttemptsAsync(int applicationId, DateTimeOffset windowStart, CancellationToken ct);
}
