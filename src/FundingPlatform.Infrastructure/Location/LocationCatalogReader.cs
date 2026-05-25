using FundingPlatform.Application.Abstractions.Location;
using FundingPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FundingPlatform.Infrastructure.Location;

/// <summary>
/// Spec 025 / FR-005 — EF-backed <see cref="ILocationCatalogReader"/>. Resolves
/// the distrito → cantón → provincia chain in a single query (one indexed PK
/// lookup plus the two parent joins) over <see cref="AppDbContext"/>.
/// </summary>
public sealed class LocationCatalogReader : ILocationCatalogReader
{
    private readonly AppDbContext _db;

    public LocationCatalogReader(AppDbContext db)
    {
        _db = db;
    }

    public async Task<DistrictChain?> GetDistrictChainAsync(int districtId, CancellationToken ct = default)
    {
        var district = await _db.Districts
            .Include(d => d.Canton!)
                .ThenInclude(c => c.Province!)
            .FirstOrDefaultAsync(d => d.Id == districtId, ct);

        if (district?.Canton?.Province is null)
        {
            return null;
        }

        var canton = district.Canton;
        var province = canton.Province;

        return new DistrictChain(
            ProvinceId: province.Id,
            ProvinceName: province.Name,
            CantonId: canton.Id,
            CantonName: canton.Name,
            DistrictId: district.Id,
            DistrictName: district.Name,
            Canton: canton,
            District: district);
    }
}
