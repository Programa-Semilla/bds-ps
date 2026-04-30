namespace FundingPlatform.Application.Suppliers.Services;

/// <summary>
/// Discriminated result of CreateDraftWithBranchAsync.
///   Success:           supplier created cleanly; SupplierId is the new row.
///   RetryWithExisting: a unique-constraint collision (race with another applicant)
///                      means the supplier now exists; SupplierId is the existing row.
///                      Caller should redirect to the existing-supplier flow per R4.
/// </summary>
public sealed record CreateDraftResult(CreateDraftOutcome Outcome, int SupplierId)
{
    public static CreateDraftResult Success(int newSupplierId) =>
        new(CreateDraftOutcome.Success, newSupplierId);

    public static CreateDraftResult RetryWithExisting(int existingSupplierId) =>
        new(CreateDraftOutcome.RetryWithExisting, existingSupplierId);
}

public enum CreateDraftOutcome
{
    Success,
    RetryWithExisting,
}
