using Microsoft.AspNetCore.Mvc.Rendering;

namespace FundingPlatform.Web.ViewModels;

/// <summary>
/// Spec 025 / FR-002 (was <c>ProvinceCantonCascadeViewModel</c>, spec 021) — model
/// for the three-tier Provincia → Cantón → Distrito cascade partial
/// (<c>_LocationCascade.cshtml</c>). Hosts build the Provincia list (always) plus
/// any pre-selected Cantón / Distrito lists server-side, then hand the partial a
/// ready-to-render model. The companion module (<c>location-cascade.js</c>)
/// repopulates the dependent &lt;select&gt;s on the client by calling
/// <c>/api/cantons?provinceId={id}</c> and <c>/api/districts?cantonId={id}</c>.
/// </summary>
public sealed class LocationCascadeViewModel
{
    public int? SelectedProvinceId { get; init; }
    public int? SelectedCantonId { get; init; }
    public int? SelectedDistrictId { get; init; }

    /// <summary>Form-field name for the Provincia &lt;select&gt; (model binding).</summary>
    public string ProvinceFieldName { get; init; } = "ProvinceId";

    /// <summary>Form-field name for the Cantón &lt;select&gt;. Also seeds the
    /// <c>data-cascade-target</c> selector for the province source.</summary>
    public string CantonFieldName { get; init; } = "CantonId";

    /// <summary>Form-field name for the Distrito &lt;select&gt;. Also seeds the
    /// <c>data-cascade-target</c> selector for the cantón source.</summary>
    public string DistrictFieldName { get; init; } = "DistrictId";

    /// <summary>
    /// Prefix applied to the element <c>id</c>s (and cascade-target selectors) only —
    /// NOT the binding <c>name</c>. Lets multiple cascades that bind to identically
    /// named fields coexist on one page (admin Detail renders one branch-edit form per
    /// branch, all binding <c>ProvinceId/CantonId/DistrictId</c>). Empty by default
    /// (applicant surfaces already have unique prefixed field names).
    /// </summary>
    public string ElementIdPrefix { get; init; } = string.Empty;

    /// <summary>Provinces rendered as the source &lt;select&gt; options.</summary>
    public IReadOnlyList<SelectListItem> Provinces { get; init; } = Array.Empty<SelectListItem>();

    /// <summary>Cantones for <see cref="SelectedProvinceId"/>; empty if none pre-selected.</summary>
    public IReadOnlyList<SelectListItem> Cantons { get; init; } = Array.Empty<SelectListItem>();

    /// <summary>Distritos for <see cref="SelectedCantonId"/>; empty if none pre-selected.</summary>
    public IReadOnlyList<SelectListItem> Districts { get; init; } = Array.Empty<SelectListItem>();
}
