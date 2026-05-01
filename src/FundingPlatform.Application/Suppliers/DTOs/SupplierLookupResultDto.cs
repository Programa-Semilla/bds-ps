using FundingPlatform.Domain.Enums;

namespace FundingPlatform.Application.Suppliers.DTOs;

/// <summary>
/// Discriminated result of the legal-ID lookup (spec 013, contracts §1).
/// Outcomes:
///   - Hit:      a Verified or creator-owned PendingReview/Draft supplier was found.
///   - Empty:    no matching supplier visible to the current applicant; offer the new-supplier form.
///   - Rejected: supplier exists but is in Rejected status; show the "contact admin" message.
/// </summary>
public record SupplierLookupResultDto(
    SupplierLookupOutcome Outcome,
    SupplierDetailViewDto? Supplier);

public enum SupplierLookupOutcome
{
    Hit,
    Empty,
    Rejected,
}

public record SupplierDetailViewDto(
    int Id,
    string LegalId,
    string Name,
    bool HasElectronicInvoice,
    bool IsCompliantCCSS,
    bool IsCompliantHacienda,
    bool IsCompliantSICOP,
    SupplierVerificationStatus VerificationStatus,
    int? CreatedByApplicantId,
    IReadOnlyList<SupplierBranchDto> Branches);
