namespace FundingPlatform.Application.Admin.Groups;

/// <summary>
/// Spec 016 — DTOs returned by <see cref="IGroupService"/>. Carries the bare
/// minimum the admin UI needs (catalog table + edit form pre-fill).
/// </summary>
public sealed record GroupRow(int Id, string Name, int MemberCount);

public sealed record GroupDetail(int Id, string Name);
