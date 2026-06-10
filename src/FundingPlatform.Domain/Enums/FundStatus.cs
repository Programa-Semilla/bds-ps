// Spec 029 — see specs/029-fund-entity/data-model.md (Fund entity) and research D2.

namespace FundingPlatform.Domain.Enums;

/// <summary>
/// Spec 029 / FR-005 — lifecycle status of a
/// <see cref="FundingPlatform.Domain.Entities.Fund"/>. Active Funds appear in the
/// Process-create selector and govern visible applications; Archived Funds are
/// hidden from the selectors and force-freeze every anchored application
/// (excluded from non-admin reads + read-only against mutation). Mirrors
/// <see cref="ProcessStatus"/>; persisted as TINYINT via HasConversion&lt;byte&gt;.
/// </summary>
public enum FundStatus
{
    Active = 0,
    Archived = 1,
}
