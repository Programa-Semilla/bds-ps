using System.ComponentModel.DataAnnotations;

namespace FundingPlatform.Web.ViewModels;

public class SystemConfigurationViewModel
{
    public List<SystemConfigurationEntryViewModel> Configurations { get; set; } = new();
}

public class SystemConfigurationEntryViewModel
{
    public int Id { get; set; }

    [Display(Name = "Clave")]
    public string Key { get; set; } = string.Empty;

    // Value is intentionally not [Required]: spec-021 seeds
    // Public.Landing.*.StorageKey rows with an empty-string sentinel until
    // the admin upload flow rewrites them. A [Required] attribute failed
    // server-side ModelState validation for the whole Configuration form
    // whenever such a row was present.
    [Display(Name = "Valor")]
    public string Value { get; set; } = string.Empty;

    [Display(Name = "Descripción")]
    public string? Description { get; set; }
}
