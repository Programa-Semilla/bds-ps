// Spec 021 — see specs/021-feedback-session-may13/data-model.md (Process entity).

namespace FundingPlatform.Domain.Enums;

/// <summary>
/// Spec 021 / FR-001 — lifecycle status of a <see cref="FundingPlatform.Domain.Entities.Process"/>.
/// Active processes accept new Applications and Groups; Closed processes are
/// historically frozen (no further Applications, no agreement mutation per OQ-2).
/// </summary>
public enum ProcessStatus
{
    Active = 0,
    Closed = 1,
}
