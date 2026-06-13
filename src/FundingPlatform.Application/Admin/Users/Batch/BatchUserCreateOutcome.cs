namespace FundingPlatform.Application.Admin.Users.Batch;

/// <summary>
/// Spec 034 — per-row result, discriminated by <see cref="Succeeded"/>.
/// <see cref="KeyField"/> identifies the row in the report (the email, or the
/// código de usuario when the email cell is blank). For a succeeded row,
/// <see cref="KeyField"/> carries the email used to issue the invitation.
/// </summary>
public sealed record BatchUserCreateOutcome(
    int RowNumber,
    string KeyField,
    bool Succeeded,
    string? Reason)
{
    public static BatchUserCreateOutcome Success(int rowNumber, string keyField) =>
        new(rowNumber, keyField, true, null);

    public static BatchUserCreateOutcome Error(int rowNumber, string keyField, string reason) =>
        new(rowNumber, keyField, false, reason);
}
