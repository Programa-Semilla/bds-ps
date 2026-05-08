using FundingPlatform.Application.DTOs;

namespace FundingPlatform.Web.ViewModels.Admin;

/// <summary>
/// Spec 017 / US1 — thin Web-layer adapter wrapping <see cref="AdminDashboardDto"/>.
/// Keeps the view layer free of any direct Application-layer DTO coupling at the
/// model binding seam.
/// </summary>
public sealed record AdminDashboardViewModel(AdminDashboardDto Data);
