using FundingPlatform.Domain.Entities;

namespace FundingPlatform.Application.Abstractions.Location;

/// <summary>
/// Spec 025 / FR-005 — server-side resolution of a submitted location chain.
/// Both supplier-branch write paths (applicant <c>SupplierCatalogService</c> and
/// admin <c>AdminSuppliersController</c>) call this to (a) validate that a posted
/// <c>DistrictId</c> resolves to a real distrito and to recover its parent cantón
/// + provincia (never trusting the client's claimed parent ids), and (b) build
/// the composed display string written to the legacy
/// <c>SupplierBranch.Province</c> column (FR-013).
/// </summary>
public interface ILocationCatalogReader
{
    /// <summary>
    /// Resolves the full provincia → cantón → distrito chain for a distrito id,
    /// including the <see cref="Canton"/> and <see cref="District"/> entities the
    /// aggregate's <c>SetLocation</c> invariant requires. Returns <c>null</c> for an
    /// unknown / forged district id so the caller can add an aggregated ModelState
    /// error and reject the write.
    /// </summary>
    Task<DistrictChain?> GetDistrictChainAsync(int districtId, CancellationToken ct = default);
}

/// <summary>
/// Spec 025 — the resolved provincia/cantón/distrito chain for a distrito.
/// Carries both the display fields and the loaded <see cref="Canton"/> /
/// <see cref="District"/> entities (so the aggregate invariant can be satisfied
/// without a second DB round-trip).
/// </summary>
public sealed record DistrictChain(
    int ProvinceId,
    string ProvinceName,
    int CantonId,
    string CantonName,
    int DistrictId,
    string DistrictName,
    Canton Canton,
    District District)
{
    /// <summary>
    /// FR-013 — the composed display value written to the legacy
    /// <c>SupplierBranch.Province</c> string: most-specific first.
    /// </summary>
    public string ComposedDisplay => $"{DistrictName}, {CantonName}, {ProvinceName}";
}
