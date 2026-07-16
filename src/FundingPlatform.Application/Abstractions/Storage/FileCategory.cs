using System.ComponentModel;

namespace FundingPlatform.Application.Abstractions.Storage;

/// <summary>
/// Logical bucket for stored files. The <see cref="DescriptionAttribute"/> on each
/// member is the canonical container name (Azure Blob lowercase-and-hyphenated rules).
/// FR-013: maps each category to its own container.
/// </summary>
public enum FileCategory
{
    [Description("signed-funding-agreements")]
    SignedFundingAgreement,

    [Description("supplier-catalog-imports")]
    SupplierCatalogImport,

    [Description("application-attachments")]
    ApplicationAttachment,

    [Description("generated-artifacts")]
    GeneratedArtifact,

    // Spec 021 / US7 / FR-031 — public landing slot files (Reglamento, Ejemplo
    // de cotización). Reuses the IObjectStorage path so the public-landing
    // upload/download surfaces stay aligned with the spec 014 abstraction.
    [Description("public-landing-files")]
    PublicLandingFile,

    // Spec 029 / D3 — per-Fund applicant-downloadable regulation PDF (single
    // optional document per Fund). Reuses the spec-014 IObjectStorage path.
    [Description("fund-regulations")]
    FundRegulation,

    // Spec 036 / D4 — funds-usage evidence files uploaded by in-scope
    // reviewers/admins on an AgreementExecuted application. Own container.
    [Description("funds-usage-evidence")]
    FundsUsageEvidence,

    // Spec 045 / FR-006, FR-008 — typed disbursement evidence (bank receipt +
    // invoice) uploaded by a Financial Operator against an executed agreement.
    // Own container; reuses the spec-036 storage/upload stack (20 MiB, BackendStream).
    [Description("disbursement-evidence")]
    DisbursementEvidence,
}

public static class FileCategoryExtensions
{
    public static string ContainerName(this FileCategory category) => category switch
    {
        FileCategory.SignedFundingAgreement => "signed-funding-agreements",
        FileCategory.SupplierCatalogImport => "supplier-catalog-imports",
        FileCategory.ApplicationAttachment => "application-attachments",
        FileCategory.GeneratedArtifact => "generated-artifacts",
        FileCategory.PublicLandingFile => "public-landing-files",
        FileCategory.FundRegulation => "fund-regulations",
        FileCategory.FundsUsageEvidence => "funds-usage-evidence",
        FileCategory.DisbursementEvidence => "disbursement-evidence",
        _ => throw new ArgumentOutOfRangeException(nameof(category), category, null),
    };

    /// <summary>The well-known container names for FR-013 / FR-016 / FR-027.
    /// Spec 021 adds <c>public-landing-files</c> for FR-031 landing slots.</summary>
    public static IReadOnlyList<string> AllContainerNames { get; } =
    [
        "signed-funding-agreements",
        "supplier-catalog-imports",
        "application-attachments",
        "generated-artifacts",
        "public-landing-files",
        "fund-regulations",
        "funds-usage-evidence",
        "disbursement-evidence",
    ];
}
