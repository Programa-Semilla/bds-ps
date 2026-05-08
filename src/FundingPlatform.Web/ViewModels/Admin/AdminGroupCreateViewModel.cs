using System.ComponentModel.DataAnnotations;
using FundingPlatform.Web.Resources;

namespace FundingPlatform.Web.ViewModels.Admin;

/// <summary>Spec 016 / FR-001 — admin Groups create form payload.</summary>
public class AdminGroupCreateViewModel
{
    [Required(ErrorMessageResourceType = typeof(AdminGroupsResources),
              ErrorMessageResourceName = nameof(AdminGroupsResources.NameRequired))]
    [StringLength(100,
        ErrorMessageResourceType = typeof(AdminGroupsResources),
        ErrorMessageResourceName = nameof(AdminGroupsResources.NameTooLong))]
    [Display(Name = "Nombre del grupo")]
    public string Name { get; set; } = "";
}
