namespace FundingPlatform.Web.ViewModels.Admin;

/// <summary>
/// Spec 015 / US3 — single row of the admin currency catalog list.
/// Maps to the contract shape in <c>contracts/currency-api.md</c>.
/// </summary>
public class AdminCurrencyRowViewModel
{
    public string Code { get; set; } = string.Empty;
    public string Symbol { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool IsEnabled { get; set; }
    public bool IsBaseCurrency { get; set; }
    public short DisplayOrder { get; set; }
}

public class AdminCurrenciesListViewModel
{
    public List<AdminCurrencyRowViewModel> Rows { get; set; } = new();
}
