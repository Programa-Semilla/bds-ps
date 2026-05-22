// Spec 021 — see specs/021-feedback-session-may13/tasks.md T093
// and contracts/applicant-routes.md (POST /api/applications/suppliers/create-branch).

namespace FundingPlatform.Application.Suppliers;

/// <summary>
/// Spec 021 / T093 / FR-009 / FR-012 / FR-014 — inline applicant flow:
/// register a new <c>SupplierBranch</c> attached to an existing
/// supplier (or a new Draft supplier) including ContactPersonName +
/// Province → Cantón. The handler validates that the chosen Cantón belongs
/// to the chosen Province (the domain `SupplierBranch.SetLocation` guard
/// fires).
///
/// <para>Two flows:</para>
/// <list type="bullet">
///   <item><c>SupplierId is not null</c> → add a branch to the existing supplier.</item>
///   <item><c>SupplierId is null</c> → create a Draft supplier (legalId + name required)
///         with a default branch carrying the supplied location + contact.</item>
/// </list>
/// </summary>
public sealed record CreateSupplierBranchCommand(
    int? SupplierId,
    string? LegalId,
    string? SupplierName,
    string BranchName,
    string? ContactPersonName,
    string? Email,
    string? Phone,
    string? AddressLine,
    int ProvinceId,
    int CantonId,
    int CurrentApplicantId);

public sealed record CreateSupplierBranchResult(int SupplierId, int BranchId);

public interface ICreateSupplierBranchHandler
{
    Task<CreateSupplierBranchResult> HandleAsync(
        CreateSupplierBranchCommand cmd, CancellationToken ct = default);
}
