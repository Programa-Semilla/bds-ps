namespace FundingPlatform.Application.DTOs;

public record ItemDto(
    int Id,
    string ProductName,
    int CategoryId,
    string CategoryName,
    string TechnicalSpecifications,
    List<QuotationDto> Quotations,
    ImpactDto? Impact,
    string? ReviewComment,
    // Spec 015 / T413 — surfaces the reviewer's selected supplier so the
    // application-summary total can pick the converted-CRC amount of the
    // chosen quotation per Item. Null on Draft items (none chosen yet).
    int? SelectedSupplierId = null,
    // Drives the localized "no técnicamente equivalente" message on the
    // applicant Details page (the English ReviewComment is no longer persisted).
    bool IsNotTechnicallyEquivalent = false);
