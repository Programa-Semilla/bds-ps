// Spec 021 — see specs/021-feedback-session-may13/data-model.md (Canton catalog).

namespace FundingPlatform.Domain.Entities;

/// <summary>
/// Spec 021 / FR-014 — a Costa Rican cantón scoped under a <see cref="Province"/>.
/// ~82 rows seeded via PostDeployment MERGE. <c>SupplierBranch.CantonId</c>
/// references this row; the cross-FK invariant (<c>Canton.ProvinceId =
/// SupplierBranch.ProvinceId</c>) is enforced on the branch aggregate.
/// </summary>
public class Canton
{
    public int Id { get; private set; }
    public int ProvinceId { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;

    public Province? Province { get; private set; }

    private Canton() { }

    public Canton(int provinceId, string code, string name)
    {
        if (provinceId <= 0)
        {
            throw new ArgumentException("ProvinceId must be a positive integer.", nameof(provinceId));
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ProvinceId = provinceId;
        Code = code.Trim();
        Name = name.Trim();
    }
}
