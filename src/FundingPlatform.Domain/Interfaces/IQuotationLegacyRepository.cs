using FundingPlatform.Domain.Entities;

namespace FundingPlatform.Domain.Interfaces;

/// <summary>
/// Spec 015 / US6 — read/write surface for the legacy-quotation review queue.
/// Narrowly-scoped to the operations the
/// <c>LegacyQuotationRateAttachService</c> needs so the Application project
/// stays free of any direct <c>AppDbContext</c> dependency.
/// </summary>
public interface IQuotationLegacyRepository
{
    /// <summary>
    /// Loads a tracked <see cref="Quotation"/> by id (so the entity's
    /// <c>AttachLegacyRate</c> mutations are picked up by the change tracker).
    /// </summary>
    Task<Quotation?> GetByIdAsync(int quotationId, CancellationToken ct = default);

    /// <summary>
    /// Returns the flagged-quotation queue rows enriched with the supplier/item
    /// display data the admin needs to pick a historical rate. Ordered by oldest
    /// CreatedAt first.
    /// </summary>
    Task<IReadOnlyList<LegacyQuotationRow>> ListFlaggedAsync(CancellationToken ct = default);

    /// <summary>Persists the staged Quotation + ExchangeRate mutations.</summary>
    Task SaveChangesAsync(CancellationToken ct = default);
}

/// <summary>
/// Internal display row carrying the joined display data for the admin queue.
/// The Application service maps this to a public DTO before crossing the layer
/// boundary.
/// </summary>
public sealed record LegacyQuotationRow(
    int QuotationId,
    int ApplicationId,
    int ItemId,
    string ItemName,
    string SupplierName,
    decimal Price,
    string Currency,
    DateTime CreatedAt);
