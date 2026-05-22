using Microsoft.AspNetCore.Mvc.Rendering;

namespace FundingPlatform.Web.ViewModels;

/// <summary>
/// Spec 021 / T056 / R-4 / FR-013 — model for the Province → Cantón cascade
/// partial. Hosts (applicant supplier-branch form, admin supplier-branch form)
/// build the Province list and any pre-selected Cantón list server-side, then
/// hand the partial a ready-to-render model. The companion JS module
/// (`province-canton-cascade.js`) repopulates the Cantón <select> on the
/// client by calling `/api/cantons?provinceId={id}`.
/// </summary>
public sealed class ProvinceCantonCascadeViewModel
{
    public int? SelectedProvinceId { get; init; }
    public int? SelectedCantonId { get; init; }

    /// <summary>
    /// Form-field name for the Province <select>. Used by ASP.NET model binding.
    /// </summary>
    public string ProvinceFieldName { get; init; } = "ProvinceId";

    /// <summary>
    /// Form-field name for the Cantón <select>. Also seeds the
    /// `data-cascade-target` selector wired to `province-canton-cascade.js`.
    /// </summary>
    public string CantonFieldName { get; init; } = "CantonId";

    /// <summary>
    /// Provinces rendered as the source <select> options. The caller loads
    /// these from <see cref="FundingPlatform.Infrastructure.Persistence.AppDbContext.Provinces"/>.
    /// </summary>
    public IReadOnlyList<SelectListItem> Provinces { get; init; } = Array.Empty<SelectListItem>();

    /// <summary>
    /// Cantones for <see cref="SelectedProvinceId"/>; empty if no province
    /// is pre-selected. The JS module replaces these on province change.
    /// </summary>
    public IReadOnlyList<SelectListItem> Cantons { get; init; } = Array.Empty<SelectListItem>();
}
