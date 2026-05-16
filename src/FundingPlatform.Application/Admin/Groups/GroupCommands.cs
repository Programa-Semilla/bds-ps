namespace FundingPlatform.Application.Admin.Groups;

/// <summary>
/// Spec 016 / 021 — DTOs returned by <see cref="IGroupService"/>. Carries the
/// bare minimum the admin UI needs (catalog table + edit form pre-fill). The
/// owning Process (FR-001) is projected on every row so the Groups index can
/// surface it as a column.
/// </summary>
public sealed record GroupRow(int Id, string Name, int MemberCount, int ProcessId, string ProcessName);

public sealed record GroupDetail(int Id, string Name, int ProcessId);
