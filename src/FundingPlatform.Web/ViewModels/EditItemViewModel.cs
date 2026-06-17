namespace FundingPlatform.Web.ViewModels;

/// <summary>
/// Spec 035 / US2 — mirrors <see cref="AddItemViewModel"/> for an existing item,
/// adding the item id. Inherits all field/option members.
/// </summary>
public class EditItemViewModel : AddItemViewModel
{
    public int Id { get; set; }
}
