using FundingPlatform.Application.DTOs;

namespace FundingPlatform.Web.ViewModels;

/// <summary>
/// Spec 018 — Razor projection contract for the branded "Informe de evaluación
/// de solicitudes de desembolso" PDF. Replaces the prior funder-block-driven
/// shape (FR-019..FR-023). The funder identity is hardcoded inside the sworn
/// declaration partial; applicant email / phone / legal id and the agreement
/// reference are no longer rendered.
/// </summary>
public class FundingAgreementDocumentViewModel
{
    // Cover page (FR-005, FR-006)
    public string CompanyName { get; set; } = string.Empty;
    public string ApplicantRepresentativeName { get; set; } = string.Empty;
    /// <summary>
    /// Spec 021 / FR-008 / OQ-4 — opaque <c>Application.PublicCode</c> (e.g.
    /// <c>A7K2-9XF</c>) surfaced on the Funding Agreement PDF cover. Replaces
    /// the legacy <c>Solicitud N.º {Id}</c> token (template field swap, not a
    /// footnote — see research.md OQ-4). Empty string when an Application has
    /// not yet been stamped (defensive fallback; production flow always stamps
    /// before the agreement is rendered).
    /// </summary>
    public string PublicCode { get; set; } = string.Empty;
    public DateTime GeneratedAtUtc { get; set; }
    /// <summary>es-CR long form, e.g. "8 de mayo de 2026".</summary>
    public string GenerationDateLong { get; set; } = string.Empty;
    /// <summary>Distinct names of users who took at least one ReviewItem action.</summary>
    public IReadOnlyList<string> CommissionMembers { get; set; } = Array.Empty<string>();

    // Localisation
    public string LocaleCode { get; set; } = "es-CR";
    public string CurrencyIsoCode { get; set; } = "CRC";

    // Requested resources (FR-008)
    public IReadOnlyList<RequestedResourceRow> RequestedResources { get; set; }
        = Array.Empty<RequestedResourceRow>();

    // Committee results (FR-009)
    public IReadOnlyList<ApprovedLineRow> ApprovedLines { get; set; }
        = Array.Empty<ApprovedLineRow>();
    public IReadOnlyList<RejectedLineRow> RejectedLines { get; set; }
        = Array.Empty<RejectedLineRow>();
    /// <summary>Pre-composed "Se aprueban las líneas …" sentence.</summary>
    public string ApprovedSummaryParagraph { get; set; } = string.Empty;
    public decimal ApprovedDisbursementTotal { get; set; }

    // Supplier verification (FR-010)
    public IReadOnlyList<SupplierComplianceRow> SupplierCompliance { get; set; }
        = Array.Empty<SupplierComplianceRow>();

    /// <summary>
    /// Engine-level pre-flight contract used by
    /// <c>SyncfusionFundingAgreementPdfRenderer.EnsureConversionMetadata</c> to
    /// flag missing rate snapshots before Razor work runs. This survives the
    /// view-model rewrite per T012/T019; the new <see cref="RequestedResources"/>
    /// is the Razor-facing shape.
    /// </summary>
    public IReadOnlyList<FundingAgreementItemRowDto> Items { get; set; }
        = Array.Empty<FundingAgreementItemRowDto>();
}

/// <summary>FR-008 — one row of the `Recursos solicitados` table.</summary>
public sealed record RequestedResourceRow(
    string LineCode,                  // Variable column
    string ProductName,               // Tipo
    string CategoryName,              // Descripción
    decimal Amount,                   // Monto (CRC)
    string Currency,                  // ISO code of original quotation currency
    string SelectedSupplierName,      // Empresa seleccionada
    string? CurrencyConversionNote,   // Spec 015 conversion note (CRC lines null)
    // Spec 035 / D9 — per-line category field values + impact rendered as a block.
    IReadOnlyList<RequestedResourceDetail>? CategoryFields = null,
    string? ImpactTemplateName = null,
    IReadOnlyList<RequestedResourceDetail>? ImpactParameters = null);

/// <summary>Spec 035 / D9 — a label/value pair beneath a funding-agreement line.</summary>
public sealed record RequestedResourceDetail(string Label, string? Value);

/// <summary>FR-009 / FR-011 — one row of the approved-lines subtable.</summary>
public sealed record ApprovedLineRow(
    string AcuerdoLabel,              // Acuerdo (e.g. "FI_SBDCR25-002")
    string LineCode,                  // Detalle / Variable
    string ProductName,               // Tipo
    string SelectedSupplierName,      // Empresa proveedora
    decimal Disbursement,             // Desembolso (CRC)
    string? CurrencyConversionNote);

/// <summary>FR-009 — one row of the rejected-lines subtable.</summary>
public sealed record RejectedLineRow(
    string AcuerdoLabel,
    string LineCode,
    string ProductName,
    string Motivo);                   // ItemResponse rejection reason

/// <summary>FR-010 — one row of the `Información empresas proveedoras` table.</summary>
public sealed record SupplierComplianceRow(
    DateTime ReviewedAt,              // Fecha de revisión
    string SupplierName,              // Empresa proveedora
    string Hacienda,
    string Ccss,
    string Sicop);
