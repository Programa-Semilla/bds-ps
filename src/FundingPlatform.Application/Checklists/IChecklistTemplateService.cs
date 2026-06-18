using FundingPlatform.Domain.Enums;

namespace FundingPlatform.Application.Checklists;

/// <summary>
/// Spec 040 / US4 — admin lifecycle for the <see cref="FundingPlatform.Domain.Entities.ChecklistTemplate"/>
/// aggregate. Every mutation writes an <c>AdminAuditEvent</c> (checklist.*) in the same
/// UnitOfWork (mirrors <c>IFundService</c>). At most one template is active per
/// <see cref="ChecklistStage"/> value; <see cref="GetActiveForStageAsync"/> resolves the
/// effective template for a stage (stage-specific beats <see cref="ChecklistStage.Both"/>).
/// </summary>
public interface IChecklistTemplateService
{
    Task<IReadOnlyList<ChecklistTemplateRow>> ListAsync(
        ChecklistStage? stageFilter, bool? activeFilter, CancellationToken ct);

    Task<ChecklistTemplateDetail?> GetDetailAsync(int id, CancellationToken ct);

    Task<int> CreateAsync(CreateChecklistTemplateCommand command, string actorUserId, CancellationToken ct);

    /// <summary>Edits name/description/stage and full-replaces the item set (Category pattern).</summary>
    Task EditAsync(EditChecklistTemplateCommand command, string actorUserId, CancellationToken ct);

    /// <summary>Activates the template, deactivating any other active template with the same stage.</summary>
    Task ActivateAsync(int id, string actorUserId, CancellationToken ct);

    Task DeactivateAsync(int id, string actorUserId, CancellationToken ct);
}

/// <summary>One posted/edited checklist item (ordering is the list position).</summary>
public sealed record ChecklistItemInput(string Text, bool IsRequired);

public sealed record CreateChecklistTemplateCommand(
    string Name, string? Description, ChecklistStage AppliesToStage, bool Activate,
    IReadOnlyList<ChecklistItemInput> Items);

public sealed record EditChecklistTemplateCommand(
    int Id, string Name, string? Description, ChecklistStage AppliesToStage,
    IReadOnlyList<ChecklistItemInput> Items);

/// <summary>Index row: name, stage, active, item count.</summary>
public sealed record ChecklistTemplateRow(int Id, string Name, ChecklistStage AppliesToStage, bool IsActive, int ItemCount);

public sealed record ChecklistTemplateItemRow(int Id, string Text, int DisplayOrder, bool IsRequired);

public sealed record ChecklistTemplateDetail(
    int Id, string Name, string? Description, ChecklistStage AppliesToStage, bool IsActive,
    IReadOnlyList<ChecklistTemplateItemRow> Items);
