namespace FundingPlatform.Domain.Enums;

/// <summary>
/// Spec 040 / D4 — the workflow stage a checklist applies to. A
/// <see cref="Entities.ChecklistTemplate"/> declares <c>AppliesToStage</c>
/// (Reviewer, Auditor, or Both); an <see cref="Entities.ApplicationChecklistResponse"/>
/// records the concrete stage it was completed in (Reviewer or Auditor only — never
/// Both). "Active template for stage X" = the active template whose AppliesToStage is
/// X or Both, stage-specific winning over Both.
/// </summary>
public enum ChecklistStage
{
    Reviewer = 1,
    Auditor = 2,
    Both = 3
}
