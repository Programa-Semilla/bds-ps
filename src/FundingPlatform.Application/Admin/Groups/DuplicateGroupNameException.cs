namespace FundingPlatform.Application.Admin.Groups;

/// <summary>
/// Spec 016 / FR-001 — surfaced when the database rejects a duplicate group name
/// (UX_Groups_Name unique index). Translated to the
/// `AdminGroupsResources.NameAlreadyInUse` ModelState error by the controller.
/// </summary>
public sealed class DuplicateGroupNameException : Exception
{
    public string AttemptedName { get; }

    public DuplicateGroupNameException(string attemptedName)
        : base($"A group with name '{attemptedName}' already exists.")
    {
        AttemptedName = attemptedName;
    }
}
