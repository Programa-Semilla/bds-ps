using FundingPlatform.Application.Abstractions.AiComparison;

namespace FundingPlatform.Application.AiComparison.Commands;

/// <summary>
/// Spec 020 / Phase 3 — thin wrapper invoked by the controller. Resolves the
/// orchestrator and routes typed exceptions to the documented error envelopes.
/// </summary>
public class GenerateComparisonCommandHandler
{
    private readonly IComparisonOrchestrator _orchestrator;

    public GenerateComparisonCommandHandler(IComparisonOrchestrator orchestrator)
    {
        _orchestrator = orchestrator;
    }

    public Task<GenerateComparisonResult> HandleAsync(
        GenerateComparisonCommand command, CancellationToken ct)
        => _orchestrator.GenerateAsync(command, ct);
}
