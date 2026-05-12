using FundingPlatform.Application.Abstractions.AiComparison;
using FundingPlatform.Domain.Entities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FundingPlatform.Infrastructure.AiComparison;

/// <summary>
/// Spec 020 / FR-F1, FR-F4 — drains the ComparisonJobs queue at the configured
/// concurrency (default 2). Each claimed job runs the orchestrator against
/// the stub or live provider. Failures route to <c>RecordFailure</c> with the
/// canonical failure reason; successes update <c>ResultingArtifactId</c>.
/// </summary>
public class ComparisonJobWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ComparisonJobWorker> _logger;

    public ComparisonJobWorker(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<ComparisonJobWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var concurrency = int.TryParse(_configuration["AiComparison:WorkerConcurrency"], out var c) ? c : 2;
        using var sem = new SemaphoreSlim(concurrency, concurrency);

        while (!stoppingToken.IsCancellationRequested)
        {
            await sem.WaitAsync(stoppingToken);
            _ = Task.Run(async () =>
            {
                try
                {
                    var claimed = await ClaimAndRunOneAsync(stoppingToken);
                    if (!claimed)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
                    }
                }
                catch (OperationCanceledException) { /* shutting down */ }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unexpected ComparisonJobWorker iteration failure.");
                }
                finally
                {
                    sem.Release();
                }
            }, stoppingToken);
        }
    }

    /// <summary>Returns true when a job was claimed (success or failure), false when the queue is empty.</summary>
    private async Task<bool> ClaimAndRunOneAsync(CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var jobs = scope.ServiceProvider.GetRequiredService<IComparisonJobRepository>();
        var orchestrator = scope.ServiceProvider.GetRequiredService<IComparisonOrchestrator>();

        var job = await jobs.ClaimNextPendingAsync(DateTimeOffset.UtcNow, ct);
        if (job is null) return false;

        try
        {
            var result = await orchestrator.GenerateAsync(new GenerateComparisonCommand(
                ApplicationItemId: job.ApplicationItemId,
                ActorUserId: job.RequestedByUserId,
                ActorRole: "Reviewer",
                BypassRateLimit: job.BypassedRateLimit,
                BypassTokenCap: job.BypassedTokenCap,
                ForceRegenerate: true), ct);

            switch (result)
            {
                case GenerateComparisonSuccess:
                    job.RecordSuccess(job.ApplicationItemId, DateTimeOffset.UtcNow);
                    break;
                case GenerateComparisonFailure f:
                    job.RecordFailure(f.FailureReason ?? "unknown", DateTimeOffset.UtcNow);
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ComparisonJobWorker job {JobId} failed.", job.Id);
            try { job.RecordFailure("worker_crashed", DateTimeOffset.UtcNow); }
            catch { /* already terminal */ }
        }

        await jobs.UpdateAsync(job, ct);
        return true;
    }
}
