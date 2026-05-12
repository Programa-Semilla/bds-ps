using FundingPlatform.Application.AiComparison;
using FundingPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FundingPlatform.Infrastructure.AiComparison;

/// <summary>
/// Spec 020 / FR-G1 — counts AdminAuditEvent rows whose Action ∈
/// {AiComparisonGenerated, AiComparisonFailed} in the rolling 24h window for
/// the given application. Uses a TargetType filter to keep the query selective.
/// </summary>
public class AdminAuditRateLimitCounter : IRateLimitCounter
{
    private static readonly string[] ActionsCounted =
    {
        "AiComparisonGenerated",
        "AiComparisonFailed",
    };

    private readonly AppDbContext _context;

    public AdminAuditRateLimitCounter(AppDbContext context)
    {
        _context = context;
    }

    public async Task<int> CountAttemptsAsync(int applicationId, DateTimeOffset windowStart, CancellationToken ct)
    {
        // PayloadJson contains "applicationId":<int>. We do a string contains
        // filter on the JSON column to avoid a JSON parsing function for SQL
        // Server compatibility across environments.
        var needle = "\"applicationId\":" + applicationId;

        return await _context.AdminAuditEvents
            .Where(e => ActionsCounted.Contains(e.Action))
            .Where(e => e.OccurredAt >= windowStart)
            .Where(e => e.PayloadJson != null && EF.Functions.Like(e.PayloadJson!, "%" + needle + "%"))
            .CountAsync(ct);
    }
}
