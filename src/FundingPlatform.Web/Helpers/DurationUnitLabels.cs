using FundingPlatform.Domain.Enums;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace FundingPlatform.Web.Helpers;

/// <summary>
/// Spec 039 — single source of truth for the es-CR labels of the
/// <see cref="DurationUnit"/> enum used by the delivery-lead-time and warranty
/// selects on the quote form. The DB stores only the numeric code; this resolver
/// turns it into display text (no Spanish literals in the domain or in JS).
/// </summary>
public static class DurationUnitLabels
{
    private static readonly IReadOnlyDictionary<DurationUnit, string> Map = new Dictionary<DurationUnit, string>
    {
        [DurationUnit.Days] = "días",
        [DurationUnit.Months] = "meses",
    };

    public static string Label(DurationUnit unit) =>
        Map.TryGetValue(unit, out var v) ? v : unit.ToString();

    public static IEnumerable<SelectListItem> Items(DurationUnit selected) =>
        Map.Select(kv => new SelectListItem(
            kv.Value,
            ((byte)kv.Key).ToString(System.Globalization.CultureInfo.InvariantCulture),
            kv.Key == selected));
}
