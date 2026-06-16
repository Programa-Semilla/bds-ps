namespace FundingPlatform.Application.DTOs;

public record ItemDto(
    int Id,
    string ProductName,
    int CategoryId,
    string CategoryName,
    List<QuotationDto> Quotations,
    // Spec 035 (evolved 2026-06-16, D14) — names of the application impacts this
    // line item is attributed to (one or more), and the short justification.
    List<string> AttributedImpactNames,
    string? ImpactJustification,
    // Spec 035 / D1 — per-item category field label/value pairs (replaces the
    // free-text TechnicalSpecifications).
    List<CategoryFieldValueDto> CategoryFields,
    string? ReviewComment,
    // Spec 015 / T413 — surfaces the reviewer's selected supplier so the
    // application-summary total can pick the converted-CRC amount of the
    // chosen quotation per Item. Null on Draft items (none chosen yet).
    int? SelectedSupplierId = null,
    // Drives the localized "no técnicamente equivalente" message on the
    // applicant Details page (the English ReviewComment is no longer persisted).
    bool IsNotTechnicallyEquivalent = false);

/// <summary>
/// Spec 035 / D1 — one category-field label/value pair captured on a line item,
/// rendered on every application surface in es-CR.
/// </summary>
public record CategoryFieldValueDto(string Label, string? Value);
