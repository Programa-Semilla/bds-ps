// Spec 025 — see specs/025-supplier-location-cascade/data-model.md (District catalog).

namespace FundingPlatform.Domain.Entities;

/// <summary>
/// Spec 025 / FR-001 — a Costa Rican distrito scoped under a <see cref="Canton"/>.
/// ~488 rows seeded via PostDeployment MERGE. <c>SupplierBranch.DistrictId</c>
/// references this row; the cross-FK invariant (<c>District.CantonId =
/// SupplierBranch.CantonId</c>) is enforced on the branch aggregate
/// (<see cref="SupplierBranch.SetLocation"/>). Mirrors <see cref="Canton"/>.
/// </summary>
public class District
{
    public int Id { get; private set; }
    public int CantonId { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;

    public Canton? Canton { get; private set; }

    private District() { }

    public District(int cantonId, string code, string name)
    {
        if (cantonId <= 0)
        {
            throw new ArgumentException("CantonId must be a positive integer.", nameof(cantonId));
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        CantonId = cantonId;
        Code = code.Trim();
        Name = name.Trim();
    }
}
