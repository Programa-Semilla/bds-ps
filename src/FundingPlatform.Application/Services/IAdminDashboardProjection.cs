using FundingPlatform.Application.DTOs;

namespace FundingPlatform.Application.Services;

/// <summary>
/// Spec 017 / US1 / R4 — projects the admin dashboard view model from existing
/// aggregates. No schema changes. Sub-projection failures degrade to <c>0</c>
/// per R2.
/// </summary>
public interface IAdminDashboardProjection
{
    Task<AdminDashboardDto> GetAsync(CancellationToken ct);
}
