using System.ComponentModel.DataAnnotations;
using FundingPlatform.Domain.Enums;

namespace FundingPlatform.Web.ViewModels.Admin;

/// <summary>Spec 040 / US4 — the checklist-templates admin list.</summary>
public class ChecklistAdminViewModel
{
    public List<ChecklistListItemViewModel> Templates { get; set; } = [];
    public ChecklistStage? StageFilter { get; set; }
    public bool? ActiveFilter { get; set; }
}

public class ChecklistListItemViewModel
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public ChecklistStage AppliesToStage { get; set; }
    public bool IsActive { get; set; }
    public int ItemCount { get; set; }
}

/// <summary>One repeating checklist item in the create/edit editor.</summary>
public class ChecklistItemViewModel
{
    public string Text { get; set; } = string.Empty;
    public bool IsRequired { get; set; } = true;
}

public class CreateChecklistViewModel
{
    [Required(ErrorMessage = "El nombre es obligatorio.")]
    [StringLength(200)]
    public string Name { get; set; } = string.Empty;

    [StringLength(500)]
    public string? Description { get; set; }

    public ChecklistStage AppliesToStage { get; set; } = ChecklistStage.Both;

    public bool Activate { get; set; } = true;

    public List<ChecklistItemViewModel> Items { get; set; } = [];
}

public class EditChecklistViewModel
{
    public int Id { get; set; }

    [Required(ErrorMessage = "El nombre es obligatorio.")]
    [StringLength(200)]
    public string Name { get; set; } = string.Empty;

    [StringLength(500)]
    public string? Description { get; set; }

    public ChecklistStage AppliesToStage { get; set; } = ChecklistStage.Both;

    public bool IsActive { get; set; }

    public List<ChecklistItemViewModel> Items { get; set; } = [];
}
