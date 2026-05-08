namespace FundingPlatform.Application.DTOs;

/// <summary>
/// Spec 015 / US6 — display row for the admin "Cotizaciones Pendientes" queue.
/// Surfaces enough context for an administrator to pick a historical rate for a
/// pre-spec-015 quotation that came in without a snapshot.
/// </summary>
public record LegacyQuotationDto(
    int QuotationId,
    int ApplicationId,
    int ItemId,
    string ItemName,
    string SupplierName,
    decimal Price,
    string Currency,
    DateTime CreatedAt);
