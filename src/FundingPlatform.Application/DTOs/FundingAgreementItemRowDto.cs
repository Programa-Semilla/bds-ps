namespace FundingPlatform.Application.DTOs;

/// <summary>
/// One line on the funding-agreement document.
///
/// Spec 015 / US5 (T511) — the originally-quoted currency / amount stays on the
/// row for context, but the legally-meaningful PDF renders the CRC value and a
/// per-line conversion note. <see cref="ConvertedCrcAmount"/> /
/// <see cref="SnapshotRateValue"/> / <see cref="SnapshotRateType"/> /
/// <see cref="SnapshotEffectiveAtUtc"/> are populated for non-CRC lines and
/// drive the "Conversión: 1 USD = ₡{rate} (Tipo Compra, vigente desde {date})"
/// row beneath the line.
///
/// CRC lines leave the snapshot fields null and the renderer omits the note.
/// </summary>
public record FundingAgreementItemRowDto(
    int ItemId,
    string ProductName,
    string CategoryName,
    string SupplierName,
    decimal UnitPrice,
    decimal LineTotal,
    string Currency,
    int? QuotationId = null,
    decimal? ConvertedCrcAmount = null,
    decimal? SnapshotRateValue = null,
    string? SnapshotRateType = null,
    DateTime? SnapshotEffectiveAtUtc = null,
    // Spec 018 / FR-008 — reviewer-assigned LineCode surfaces in the Funding
    // Agreement PDF tables. Carried on the DTO so the renderer-level conversion
    // pre-flight can identify rows by code in diagnostics.
    string? LineCode = null);
