using Microsoft.Extensions.Options;

namespace FundingPlatform.Application.Abstractions.Storage;

/// <summary>
/// Configuration POCO bound from the <c>Storage:</c> section. Implements
/// FR-004, FR-005, FR-007, FR-008, FR-011, FR-019, FR-020, FR-021, FR-023.
/// </summary>
public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    public const long DefaultMaxSizeBytes20Mib = 20L * 1024 * 1024;
    public const long DefaultMaxSizeBytes50Mib = 50L * 1024 * 1024;
    public const int DefaultUrlExpirySeconds = 15 * 60; // 15 min cap (FR-019)
    public const int MaxUrlExpirySeconds = 15 * 60;

    /// <summary>One of <c>AzureBlob</c>, <c>Azurite</c>, <c>LocalFilesystem</c>.</summary>
    public string Provider { get; set; } = "Azurite";

    /// <summary>Connection string. Honored only outside Production (FR-011).</summary>
    public string? ConnectionString { get; set; }

    /// <summary>Aspire resource reference (logical name) when running with managed identity.</summary>
    public string? AccountReference { get; set; }

    /// <summary>FR-020 streaming threshold. Default 1 MiB.</summary>
    public long StreamingThresholdBytes { get; set; } = 1 * 1024 * 1024;

    public StorageRetryBudgetOptions RetryBudget { get; set; } = new();

    public StorageLocalFilesystemOptions LocalFilesystem { get; set; } = new();

    public StorageCategoriesOptions Categories { get; set; } = new();

    public StorageTestFallbackOptions TestFallback { get; set; } = new();
}

public sealed class StorageRetryBudgetOptions
{
    /// <summary>Hard cap on retry attempts (FR-edge "Connection failure"). Default 3.</summary>
    public int MaxAttempts { get; set; } = 3;

    /// <summary>Total retry budget per operation in seconds. Default 30.</summary>
    public int BudgetSeconds { get; set; } = 30;
}

public sealed class StorageLocalFilesystemOptions
{
    public string? RootPath { get; set; }
}

public sealed class StorageTestFallbackOptions
{
    /// <summary>FR-008: gate that allows tests to fall back to LocalFilesystem.</summary>
    public bool AllowFilesystem { get; set; }
}

/// <summary>
/// Per-category configuration. The keys MUST match <see cref="FileCategory"/> member names.
/// </summary>
public sealed class StorageCategoriesOptions
{
    public StorageCategoryOptions SignedFundingAgreement { get; set; } = new()
    {
        MaxSizeBytes = StorageOptions.DefaultMaxSizeBytes20Mib,
    };

    public StorageCategoryOptions SupplierCatalogImport { get; set; } = new()
    {
        MaxSizeBytes = StorageOptions.DefaultMaxSizeBytes50Mib,
    };

    public StorageCategoryOptions ApplicationAttachment { get; set; } = new()
    {
        MaxSizeBytes = StorageOptions.DefaultMaxSizeBytes20Mib,
    };

    public StorageCategoryOptions GeneratedArtifact { get; set; } = new()
    {
        MaxSizeBytes = StorageOptions.DefaultMaxSizeBytes20Mib,
    };

    // Spec 021 / US7 / FR-031 — Reglamento + Ejemplo de cotización slot files.
    // 10 MiB cap matches the CLAUDE.md table for public-landing-files; SAS URL
    // expiry kept at 5 min default per the same table.
    public StorageCategoryOptions PublicLandingFile { get; set; } = new()
    {
        MaxSizeBytes = 10L * 1024 * 1024,
        UrlExpirySeconds = 300,
    };

    // Spec 029 / OI-2 — per-Fund regulation PDF. 20 MiB cap matches the
    // signed-funding-agreement cap; 5 min SAS expiry default.
    public StorageCategoryOptions FundRegulation { get; set; } = new()
    {
        MaxSizeBytes = StorageOptions.DefaultMaxSizeBytes20Mib,
        UrlExpirySeconds = 300,
    };

    // Spec 036 / FR-005, FR-009 — funds-usage evidence. 20 MiB cap; served via
    // BackendStream (no time-limited URL), so UrlExpirySeconds is irrelevant.
    public StorageCategoryOptions FundsUsageEvidence { get; set; } = new()
    {
        MaxSizeBytes = StorageOptions.DefaultMaxSizeBytes20Mib,
        ServingMode = ServingMode.BackendStream,
    };

    public StorageCategoryOptions For(FileCategory category) => category switch
    {
        FileCategory.SignedFundingAgreement => SignedFundingAgreement,
        FileCategory.SupplierCatalogImport => SupplierCatalogImport,
        FileCategory.ApplicationAttachment => ApplicationAttachment,
        FileCategory.GeneratedArtifact => GeneratedArtifact,
        FileCategory.PublicLandingFile => PublicLandingFile,
        FileCategory.FundRegulation => FundRegulation,
        FileCategory.FundsUsageEvidence => FundsUsageEvidence,
        _ => throw new ArgumentOutOfRangeException(nameof(category), category, null),
    };
}

public sealed class StorageCategoryOptions
{
    /// <summary>FR-021 per-category cap.</summary>
    public long MaxSizeBytes { get; set; } = StorageOptions.DefaultMaxSizeBytes20Mib;

    /// <summary>FR-017 default serving model.</summary>
    public ServingMode ServingMode { get; set; } = ServingMode.BackendStream;

    /// <summary>FR-019 URL expiry. Cap at 15 min.</summary>
    public int UrlExpirySeconds { get; set; } = StorageOptions.DefaultUrlExpirySeconds;

    /// <summary>FR-023 retention seam. Default "none".</summary>
    public string RetentionPolicy { get; set; } = "none";
}

/// <summary>FR-012: fail-fast validation for misconfigurations.</summary>
public sealed class StorageOptionsValidator : IValidateOptions<StorageOptions>
{
    private static readonly string[] KnownProviders = ["AzureBlob", "Azurite", "LocalFilesystem"];

    public ValidateOptionsResult Validate(string? name, StorageOptions options)
    {
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.Provider))
        {
            failures.Add("Storage:Provider is required.");
        }
        else if (!KnownProviders.Contains(options.Provider, StringComparer.OrdinalIgnoreCase))
        {
            failures.Add(
                $"Storage:Provider '{options.Provider}' is not a known provider. Valid: {string.Join(", ", KnownProviders)}.");
        }

        if (string.Equals(options.Provider, "LocalFilesystem", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(options.LocalFilesystem.RootPath))
            {
                failures.Add("Storage:Provider=LocalFilesystem requires Storage:LocalFilesystem:RootPath.");
            }

            // FR-edge: local provider rejects TimeLimitedUrl serving mode.
            foreach (var category in Enum.GetValues<FileCategory>())
            {
                var categoryOptions = options.Categories.For(category);
                if (categoryOptions.ServingMode == ServingMode.TimeLimitedUrl)
                {
                    failures.Add(
                        $"Storage:Categories:{category}:ServingMode=TimeLimitedUrl is not supported by LocalFilesystem provider.");
                }
            }
        }

        if (options.RetryBudget.MaxAttempts < 0)
            failures.Add("Storage:RetryBudget:MaxAttempts must be >= 0.");
        if (options.RetryBudget.BudgetSeconds < 1)
            failures.Add("Storage:RetryBudget:BudgetSeconds must be >= 1.");

        if (options.StreamingThresholdBytes < 1)
            failures.Add("Storage:StreamingThresholdBytes must be >= 1.");

        foreach (var category in Enum.GetValues<FileCategory>())
        {
            var categoryOptions = options.Categories.For(category);
            if (categoryOptions.MaxSizeBytes < 1)
                failures.Add($"Storage:Categories:{category}:MaxSizeBytes must be >= 1.");
            if (categoryOptions.UrlExpirySeconds <= 0 ||
                categoryOptions.UrlExpirySeconds > StorageOptions.MaxUrlExpirySeconds)
                failures.Add(
                    $"Storage:Categories:{category}:UrlExpirySeconds must be in (0, {StorageOptions.MaxUrlExpirySeconds}].");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
