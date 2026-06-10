namespace FundingPlatform.Application.Funds;

/// <summary>
/// Spec 029 / FR-003 — surfaced when a Fund name collides (pre-check or the
/// UX_Funds_Name unique index). The controller translates this to the es-CR
/// ModelState error "Ya existe un fondo con ese nombre."
/// </summary>
public sealed class DuplicateFundNameException : Exception
{
    public string AttemptedName { get; }

    public DuplicateFundNameException(string attemptedName)
        : base($"A fund with name '{attemptedName}' already exists.")
    {
        AttemptedName = attemptedName;
    }
}
