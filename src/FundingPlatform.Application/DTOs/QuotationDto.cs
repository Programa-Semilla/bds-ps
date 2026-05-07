namespace FundingPlatform.Application.DTOs;

public record QuotationDto(
    int Id,
    int SupplierId,
    string SupplierName,
    string SupplierLegalId,
    decimal Price,
    string Currency,
    DateOnly ValidUntil,
    int DocumentId,
    string DocumentFileName,
    // Spec 015 — multi-currency surface. Null when the quotation predates the
    // migration and has no snapshot (LegacyNeedsReview = true), or when the
    // currency is CRC (ConvertedCrcAmount = Price by definition).
    decimal? ConvertedCrcAmount = null,
    decimal? SnapshotRateValue = null,
    string? SnapshotRateType = null,
    DateTime? SnapshotEffectiveAtUtc = null,
    bool LegacyNeedsReview = false);
