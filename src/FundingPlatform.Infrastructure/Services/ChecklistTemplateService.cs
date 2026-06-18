using System.Text.Json;
using FundingPlatform.Application.Abstractions;
using FundingPlatform.Application.Checklists;
using FundingPlatform.Domain.Entities;
using FundingPlatform.Domain.Enums;
using FundingPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FundingPlatform.Infrastructure.Services;

/// <summary>
/// Spec 040 / US4 / T046 — implements <see cref="IChecklistTemplateService"/>. Mirrors
/// <c>FundService</c>: each mutation stages a <c>checklist.*</c> <c>AdminAuditEvent</c> and
/// commits in the same UnitOfWork. Edit full-replaces the item set (the spec-035 Category
/// pattern), which leaves recorded <c>ApplicationChecklistResponse</c> snapshots untouched
/// (FR-003 — they carry frozen text + a NO ACTION FK). Activation enforces at most one
/// active template per <see cref="ChecklistStage"/> value.
/// </summary>
public sealed class ChecklistTemplateService : IChecklistTemplateService
{
    private readonly AppDbContext _db;
    private readonly IAdminAuditEventWriter _audit;

    public ChecklistTemplateService(AppDbContext db, IAdminAuditEventWriter audit)
    {
        _db = db;
        _audit = audit;
    }

    public async Task<IReadOnlyList<ChecklistTemplateRow>> ListAsync(
        ChecklistStage? stageFilter, bool? activeFilter, CancellationToken ct)
    {
        var query = _db.ChecklistTemplates.AsNoTracking().AsQueryable();
        if (stageFilter is not null) query = query.Where(t => t.AppliesToStage == stageFilter);
        if (activeFilter is not null) query = query.Where(t => t.IsActive == activeFilter);

        return await query
            .OrderBy(t => t.Name)
            .Select(t => new ChecklistTemplateRow(
                t.Id, t.Name, t.AppliesToStage, t.IsActive, t.Items.Count(i => i.IsActive)))
            .ToListAsync(ct);
    }

    public async Task<ChecklistTemplateDetail?> GetDetailAsync(int id, CancellationToken ct)
    {
        var template = await _db.ChecklistTemplates.AsNoTracking()
            .Include(t => t.Items.OrderBy(i => i.DisplayOrder))
            .FirstOrDefaultAsync(t => t.Id == id, ct);
        if (template is null) return null;

        return new ChecklistTemplateDetail(
            template.Id, template.Name, template.Description, template.AppliesToStage, template.IsActive,
            template.Items.Where(i => i.IsActive)
                .OrderBy(i => i.DisplayOrder)
                .Select(i => new ChecklistTemplateItemRow(i.Id, i.Text, i.DisplayOrder, i.IsRequired))
                .ToList());
    }

    public async Task<int> CreateAsync(CreateChecklistTemplateCommand command, string actorUserId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);

        var template = new ChecklistTemplate(
            command.Name.Trim(), command.Description?.Trim(), command.AppliesToStage,
            isActive: false, createdByUserId: actorUserId);
        ApplyItems(template, command.Items);
        _db.ChecklistTemplates.Add(template);
        await _db.SaveChangesAsync(ct);

        await _audit.WriteAsync(
            AdminAuditEvent.ActionChecklistCreate, actorUserId,
            JsonSerializer.Serialize(new { checklistId = template.Id, name = template.Name }), ct);
        await _db.SaveChangesAsync(ct);

        if (command.Activate)
        {
            await ActivateAsync(template.Id, actorUserId, ct);
        }

        return template.Id;
    }

    public async Task EditAsync(EditChecklistTemplateCommand command, string actorUserId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        var template = await LoadAsync(command.Id, ct);

        template.Update(command.Name.Trim(), command.Description?.Trim(), command.AppliesToStage);
        // Full-replace the item set. FR-003: existing items are DEACTIVATED (not hard-deleted)
        // so recorded ApplicationChecklistResponse rows — which reference items via a NO ACTION
        // FK and snapshot the text — survive; the new items are added fresh as active.
        template.DeactivateItems();
        ApplyItems(template, command.Items);

        await _audit.WriteAsync(
            AdminAuditEvent.ActionChecklistEdit, actorUserId,
            JsonSerializer.Serialize(new { checklistId = template.Id, name = template.Name }), ct);
        await _db.SaveChangesAsync(ct);
    }

    public async Task ActivateAsync(int id, string actorUserId, CancellationToken ct)
    {
        var template = await LoadAsync(id, ct);

        // At most one active template per stage value — deactivate other active templates
        // that share this template's AppliesToStage.
        var conflicting = await _db.ChecklistTemplates
            .Where(t => t.Id != id && t.IsActive && t.AppliesToStage == template.AppliesToStage)
            .ToListAsync(ct);
        foreach (var other in conflicting) other.Deactivate();

        template.Activate();

        await _audit.WriteAsync(
            AdminAuditEvent.ActionChecklistActivate, actorUserId,
            JsonSerializer.Serialize(new { checklistId = id }), ct);
        await _db.SaveChangesAsync(ct);
    }

    public async Task DeactivateAsync(int id, string actorUserId, CancellationToken ct)
    {
        var template = await LoadAsync(id, ct);
        template.Deactivate();

        await _audit.WriteAsync(
            AdminAuditEvent.ActionChecklistDeactivate, actorUserId,
            JsonSerializer.Serialize(new { checklistId = id }), ct);
        await _db.SaveChangesAsync(ct);
    }

    private static void ApplyItems(ChecklistTemplate template, IReadOnlyList<ChecklistItemInput> items)
    {
        var order = 1;
        foreach (var item in items ?? Array.Empty<ChecklistItemInput>())
        {
            if (string.IsNullOrWhiteSpace(item.Text)) continue;
            template.AddItem(item.Text.Trim(), order++, item.IsRequired);
        }
    }

    private async Task<ChecklistTemplate> LoadAsync(int id, CancellationToken ct)
    {
        return await _db.ChecklistTemplates
            .Include(t => t.Items)
            .FirstOrDefaultAsync(t => t.Id == id, ct)
            ?? throw new InvalidOperationException($"Checklist template {id} not found.");
    }
}
