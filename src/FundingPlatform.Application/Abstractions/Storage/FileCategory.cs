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
}

public static class FileCategoryExtensions
{
    public static string ContainerName(this FileCategory category) => category switch
    {
        FileCategory.SignedFundingAgreement => "signed-funding-agreements",
        FileCategory.SupplierCatalogImport => "supplier-catalog-imports",
        FileCategory.ApplicationAttachment => "application-attachments",
        FileCategory.GeneratedArtifact => "generated-artifacts",
        _ => throw new ArgumentOutOfRangeException(nameof(category), category, null),
    };

    /// <summary>The four well-known container names for FR-013 / FR-016 / FR-027.</summary>
    public static IReadOnlyList<string> AllContainerNames { get; } =
    [
        "signed-funding-agreements",
        "supplier-catalog-imports",
        "application-attachments",
        "generated-artifacts",
    ];
}
