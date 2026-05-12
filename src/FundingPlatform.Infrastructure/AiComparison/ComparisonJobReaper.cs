using FundingPlatform.Application.Abstractions.AiComparison;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FundingPlatform.Infrastructure.AiComparison;

/// <summary>
/// Spec 020 / edge case — reaps Running jobs older than
/// <c>AiComparison:OrphanReapAfterMinutes</c>. Marks them Failed with
/// failureReason=worker_crashed. Runs on startup and every 5 min thereafter.
/// </summary>
public class ComparisonJobReaper : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ComparisonJobReaper> _logger;

    public ComparisonJobReaper(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<ComparisonJobReaper> logger)
    {
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromMinutes(5);
        var orphanAfter = TimeSpan.FromMinutes(
            int.TryParse(_configuration["AiComparison:OrphanReapAfterMinutes"], out var m) ? m : 5);

        // First sweep on startup so a crash-recovery cycle is immediate.
        await ReapAsync(orphanAfter, stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try { await Task.Delay(interval, stoppingToken); }
            catch (TaskCanceledException) { return; }
            await ReapAsync(orphanAfter, stoppingToken);
        }
    }

    private async Task ReapAsync(TimeSpan orphanAfter, CancellationToken ct)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var jobs = scope.ServiceProvider.GetRequiredService<IComparisonJobRepository>();
            var cutoff = DateTimeOffset.UtcNow - orphanAfter;
            var orphans = await jobs.GetOrphanedRunningAsync(cutoff, ct);
            foreach (var job in orphans)
            {
                if (job.Reap(cutoff, DateTimeOffset.UtcNow))
                {
                    await jobs.UpdateAsync(job, ct);
                    _logger.LogWarning("Reaped orphan ComparisonJob {JobId} for item {ItemId}.",
                        job.Id, job.ApplicationItemId);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Reaper sweep failed.");
        }
    }
}
