using System.ComponentModel.DataAnnotations;
using FundingPlatform.Web.Resources;

namespace FundingPlatform.Web.ViewModels.Admin;

/// <summary>Spec 016 / FR-006 + Spec 021 / FR-001 — admin Groups edit form
/// payload. Carries the rename field and the owning-Process selector used to
/// reparent the Group (<c>IGroupService.MoveToProcessAsync</c>).</summary>
public class AdminGroupEditViewModel
{
    public int Id { get; set; }

    [Required(ErrorMessageResourceType = typeof(AdminGroupsResources),
              ErrorMessageResourceName = nameof(AdminGroupsResources.NameRequired))]
    [StringLength(100,
        ErrorMessageResourceType = typeof(AdminGroupsResources),
        ErrorMessageResourceName = nameof(AdminGroupsResources.NameTooLong))]
    [Display(Name = "Nombre del grupo")]
    public string Name { get; set; } = "";

    /// <summary>Spec 021 / FR-001 — the Process this Group belongs to. Changing
    /// it reparents the Group via <c>MoveToProcessAsync</c>.</summary>
    [Display(Name = "Proceso")]
    public int ProcessId { get; set; }

    /// <summary>Catalog of Processes for the reparent dropdown.</summary>
    public IReadOnlyList<AdminGroupProcessOption> ProcessOptions { get; set; } = Array.Empty<AdminGroupProcessOption>();

    /// <summary>Spec 029 — Fondo → Proceso catalog for the reparent drill-down
    /// (all Funds incl. Archived so the current Process is always reachable).</summary>
    public IReadOnlyList<FundingPlatform.Application.Admin.Filters.FundHierarchyNode> FundHierarchy { get; set; }
        = Array.Empty<FundingPlatform.Application.Admin.Filters.FundHierarchyNode>();

    /// <summary>The Fund of the currently-selected Process, pre-selecting the
    /// Fondo level of the drill-down.</summary>
    public int? SelectedFundId { get; set; }

    /// <summary>Pre-deletion member count, used to render the delete-confirm copy.</summary>
    public int MemberCount { get; set; }
}

/// <summary>Spec 021 / FR-001 — one selectable Process for the reparent dropdown.</summary>
public sealed record AdminGroupProcessOption(int Id, string Name);
