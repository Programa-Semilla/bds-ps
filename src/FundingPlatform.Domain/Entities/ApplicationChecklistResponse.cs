using FundingPlatform.Domain.Enums;

namespace FundingPlatform.Domain.Entities;

/// <summary>
/// Spec 040 / D6 — the recorded outcome of one checklist item against one application
/// at one stage. The item text is <b>frozen</b> into <see cref="ItemTextSnapshot"/> at
/// completion (FR-003 — later template edits never rewrite recorded responses; the
/// spec-037 <c>CompanyName</c> snapshot pattern). The FK to
/// <see cref="ChecklistTemplateItem"/> is NO ACTION (items are deactivated, never hard
/// deleted) so history survives. One current row per
/// <c>(ApplicationId, Stage, ChecklistTemplateItemId)</c>; each completion cycle
/// overwrites it, with cross-cycle audit living in <c>VersionHistory</c>.
/// </summary>
public class ApplicationChecklistResponse
{
    public int Id { get; private set; }
    public int ApplicationId { get; private set; }
    public ChecklistStage Stage { get; private set; }
    public int ChecklistTemplateItemId { get; private set; }
    public string ItemTextSnapshot { get; private set; } = string.Empty;
    public ChecklistResponseStatus Status { get; private set; }
    public string? NonComplianceReason { get; private set; }
    public string CompletedByUserId { get; private set; } = string.Empty;
    public DateTime CompletedAtUtc { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    private ApplicationChecklistResponse() { }

    public ApplicationChecklistResponse(
        int applicationId,
        ChecklistStage stage,
        int checklistTemplateItemId,
        string itemTextSnapshot,
        ChecklistResponseStatus status,
        string? nonComplianceReason,
        string completedByUserId)
    {
        if (stage == ChecklistStage.Both)
        {
            throw new ArgumentException(
                "A checklist response is recorded against Reviewer or Auditor, never Both.", nameof(stage));
        }
        if (status == ChecklistResponseStatus.NotCompliant
            && string.IsNullOrWhiteSpace(nonComplianceReason))
        {
            throw new ArgumentException(
                "A non-compliant checklist response requires a reason.", nameof(nonComplianceReason));
        }

        ApplicationId = applicationId;
        Stage = stage;
        ChecklistTemplateItemId = checklistTemplateItemId;
        ItemTextSnapshot = (itemTextSnapshot ?? string.Empty).Trim();
        Status = status;
        NonComplianceReason = string.IsNullOrWhiteSpace(nonComplianceReason)
            ? null
            : nonComplianceReason.Trim();
        CompletedByUserId = completedByUserId;
        CompletedAtUtc = DateTime.UtcNow;
    }
}
