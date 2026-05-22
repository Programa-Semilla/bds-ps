namespace FundingPlatform.Application.Applications.Commands;

/// <summary>
/// Spec 023 — applicant-initiated edit of a quotation's mutable fields
/// (Price / Currency / ValidUntil / SupplierBranch). Branch reassignment is
/// restricted to branches of the same Supplier (FR-004); cross-supplier
/// swaps are rejected by the entity invariant.
///
/// <see cref="ApplicantId"/> is resolved from the current user in the
/// controller and used by the service to enforce FR-007 ownership.
/// </summary>
public sealed record EditQuotationCommand
{
    public int ApplicationId { get; init; }
    public int ItemId { get; init; }
    public int QuotationId { get; init; }
    public decimal Price { get; init; }
    public string Currency { get; init; } = string.Empty;
    public DateOnly ValidUntil { get; init; }
    public int SupplierBranchId { get; init; }
    public int ApplicantId { get; init; }
}
