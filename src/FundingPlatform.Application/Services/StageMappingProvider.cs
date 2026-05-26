using FundingPlatform.Application.DTOs;

namespace FundingPlatform.Application.Services;

/// <summary>
/// Spec 011 (FR-036) — single source of truth for the canonical
/// (stage → icon, label, color-token) mapping consumed by IStatusDisplayResolver
/// and IJourneyStageResolver.
/// </summary>
public interface IStageMappingProvider
{
    IReadOnlyList<StageMapping> GetMainline();
    IReadOnlyDictionary<JourneyBranchKind, StageMapping> GetBranches();
    bool IsStageTransition(string versionHistoryAction);
    JourneyStage? StageForAction(string versionHistoryAction);
    StageMapping ForStage(JourneyStage stage);
}

public sealed record StageMapping(
    JourneyStage Stage,
    string IconKey,
    string Label,
    string ColorToken,
    string SubtleColorToken);

public sealed class StageMappingProvider : IStageMappingProvider
{
    private static readonly IReadOnlyList<StageMapping> Mainline = new List<StageMapping>
    {
        // es-CR labels (default culture). Aligned with StatusVisualMap's Spanish
        // ApplicationState pills; "Funded" uses the admin KPI wording "Fondos
        // entregados" and avoids the word "financiamiento" (FR-029 copy pivot).
        new(JourneyStage.Draft,              "ti ti-pencil",         "Borrador",              "--color-text-secondary", "--color-bg-surface-raised"),
        new(JourneyStage.Submitted,          "ti ti-send",           "Enviada",               "--color-info",           "--color-info-subtle"),
        new(JourneyStage.UnderReview,        "ti ti-eye",            "En revisión",           "--color-primary",        "--color-primary-subtle"),
        new(JourneyStage.Decision,           "ti ti-gavel",          "Decisión",              "--color-primary",        "--color-primary-subtle"),
        new(JourneyStage.AgreementGenerated, "ti ti-file-signature", "Convenio generado",     "--color-primary",        "--color-primary-subtle"),
        new(JourneyStage.Signed,             "ti ti-signature",      "Firmado",               "--color-success",        "--color-success-subtle"),
        new(JourneyStage.Funded,             "ti ti-circle-check",   "Fondos entregados",     "--color-success",        "--color-success-subtle"),
    };

    private static readonly IReadOnlyDictionary<JourneyBranchKind, StageMapping> Branches =
        new Dictionary<JourneyBranchKind, StageMapping>
        {
            [JourneyBranchKind.SentBack] = new(JourneyStage.Decision, "ti ti-arrow-back-up", "Devuelta",   "--color-warning", "--color-warning-subtle"),
            [JourneyBranchKind.Rejected] = new(JourneyStage.Decision, "ti ti-x-circle",      "Rechazada",  "--color-danger",  "--color-danger-subtle"),
            [JourneyBranchKind.Appeal]   = new(JourneyStage.Decision, "ti ti-scale",         "Apelación",  "--color-info",    "--color-info-subtle"),
        };

    private static readonly IReadOnlyDictionary<string, JourneyStage> ActionToStage =
        new Dictionary<string, JourneyStage>(StringComparer.OrdinalIgnoreCase)
        {
            ["Created"]                   = JourneyStage.Draft,
            ["Submitted"]                 = JourneyStage.Submitted,
            ["StartReview"]               = JourneyStage.UnderReview,
            ["Finalize"]                  = JourneyStage.Decision,
            ["AgreementGenerated"]        = JourneyStage.AgreementGenerated,
            ["AgreementRegenerated"]      = JourneyStage.AgreementGenerated,
            ["AgreementExecuted"]         = JourneyStage.Signed,
            ["Funded"]                    = JourneyStage.Funded,
        };

    public IReadOnlyList<StageMapping> GetMainline() => Mainline;
    public IReadOnlyDictionary<JourneyBranchKind, StageMapping> GetBranches() => Branches;

    public bool IsStageTransition(string versionHistoryAction)
        => !string.IsNullOrWhiteSpace(versionHistoryAction)
           && ActionToStage.ContainsKey(versionHistoryAction);

    public JourneyStage? StageForAction(string versionHistoryAction)
    {
        if (string.IsNullOrWhiteSpace(versionHistoryAction)) return null;
        return ActionToStage.TryGetValue(versionHistoryAction, out var stage) ? stage : (JourneyStage?)null;
    }

    public StageMapping ForStage(JourneyStage stage)
    {
        foreach (var m in Mainline)
        {
            if (m.Stage == stage) return m;
        }
        throw new ArgumentOutOfRangeException(nameof(stage), stage, "Unknown JourneyStage");
    }
}
