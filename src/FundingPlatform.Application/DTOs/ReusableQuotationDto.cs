namespace FundingPlatform.Application.DTOs;

/// <summary>
/// Spec 035 / US3 — a sibling line item's quotation offered for reuse within the
/// same application. Selecting one carries over the supplier + branch + uploaded
/// document; the applicant supplies this item's own price/currency/validity.
/// </summary>
public record ReusableQuotationDto(
    int SourceQuotationId,
    string SupplierName,
    string BranchName,
    string DocumentFileName,
    string Currency);
