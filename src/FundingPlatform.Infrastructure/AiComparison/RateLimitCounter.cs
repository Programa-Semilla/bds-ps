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
        // PayloadJson contains "applicationId":<int>. We do a substring filter
        // on the JSON column to avoid SQL Server-specific JSON functions.
        // CRITICAL: include the trailing "," to prevent prefix matches —
        // searching for "applicationId":1 must NOT match "applicationId":10
        // or "applicationId":100. The audit factory always emits applicationId
        // before bypassedRateLimit (see AdminAuditEventComparisonFactory.BuildBaseDict),
        // so the comma terminator is stable.
        var needle = "\"applicationId\":" + applicationId + ",";

        return await _context.AdminAuditEvents
            .Where(e => ActionsCounted.Contains(e.Action))
            .Where(e => e.OccurredAt >= windowStart)
            .Where(e => e.PayloadJson != null && EF.Functions.Like(e.PayloadJson!, "%" + needle + "%"))
            .CountAsync(ct);
    }
}
