using System.ComponentModel.DataAnnotations;
using FundingPlatform.Web.Resources;

namespace FundingPlatform.Web.ViewModels.Admin;

/// <summary>Spec 016 / FR-006 — admin Groups rename form payload.</summary>
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

    /// <summary>Pre-deletion member count, used to render the delete-confirm copy.</summary>
    public int MemberCount { get; set; }
}
