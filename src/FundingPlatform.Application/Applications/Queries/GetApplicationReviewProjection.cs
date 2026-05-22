// Spec 021 — see specs/021-feedback-session-may13/tasks.md T092
// and contracts/applicant-routes.md (GET /Applications/{publicCode}/Review).

using FundingPlatform.Domain.Enums;

namespace FundingPlatform.Application.Applications.Queries;

/// <summary>
/// Spec 021 / T092 / FR-017 / FR-022 — read-side projection seam for the
/// <c>/Applications/{publicCode}/Review</c> page.
///
/// <para>Renders the items list, per-item quotation summaries, totals in
/// CRC + (optional) FX disclaimer trigger, and the Application's Impact
/// summary. Infrastructure implements the EF-backed query.</para>
/// </summary>
public interface IGetApplicationReviewProjection
{
    Task<ApplicationReviewViewModel?> ExecuteAsync(
        string publicCode, int currentApplicantId, CancellationToken ct = default);
}

public sealed record ApplicationReviewViewModel(
    int Id,
    string PublicCode,
    string CompanyName,
    ApplicationState State,
    IReadOnlyList<ReviewItemRow> Items,
    decimal? TotalCrc,
    bool HasNonCrcQuotation,
    ReviewImpactSummary? Impact,
    int MinimumQuotationsPerItem,
    bool CanSubmit);

public sealed record ReviewItemRow(
    int Id,
    string ProductName,
    string CategoryName,
    string TechnicalSpecifications,
    IReadOnlyList<ReviewQuotationRow> Quotations);

public sealed record ReviewQuotationRow(
    int Id,
    int SupplierId,
    string SupplierName,
    decimal Price,
    string Currency,
    decimal? ConvertedCrcAmount);

public sealed record ReviewImpactSummary(
    int TemplateId,
    string TemplateName,
    IReadOnlyList<ReviewImpactParameter> Parameters);

public sealed record ReviewImpactParameter(string Label, string Value);
