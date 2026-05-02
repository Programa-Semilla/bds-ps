using FundingPlatform.Application.Abstractions.Storage;
using Microsoft.Extensions.Configuration;

namespace FundingPlatform.Tests.Unit.Storage;

[TestFixture]
public class StorageOptionsTests
{
    [Test]
    public void Default_values_match_documented_defaults()
    {
        var options = new StorageOptions();

        Assert.That(options.Provider, Is.EqualTo("Azurite"));
        Assert.That(options.StreamingThresholdBytes, Is.EqualTo(1 * 1024 * 1024));
        Assert.That(options.RetryBudget.MaxAttempts, Is.EqualTo(3));
        Assert.That(options.RetryBudget.BudgetSeconds, Is.EqualTo(30));
        Assert.That(options.Categories.SignedFundingAgreement.MaxSizeBytes,
            Is.EqualTo(20L * 1024 * 1024));
        Assert.That(options.Categories.SupplierCatalogImport.MaxSizeBytes,
            Is.EqualTo(50L * 1024 * 1024));
        Assert.That(options.Categories.ApplicationAttachment.MaxSizeBytes,
            Is.EqualTo(20L * 1024 * 1024));
        Assert.That(options.Categories.GeneratedArtifact.MaxSizeBytes,
            Is.EqualTo(20L * 1024 * 1024));
        Assert.That(options.Categories.SignedFundingAgreement.RetentionPolicy, Is.EqualTo("none"));
        Assert.That(options.TestFallback.AllowFilesystem, Is.False);
    }

    [Test]
    public void Validator_rejects_unknown_provider()
    {
        var validator = new StorageOptionsValidator();
        var options = new StorageOptions { Provider = "Banana" };

        var result = validator.Validate(null, options);

        Assert.That(result.Failed, Is.True);
        Assert.That(result.Failures, Has.Some.Contains("Storage:Provider"));
    }

    [Test]
    public void Validator_rejects_LocalFilesystem_without_RootPath()
    {
        var validator = new StorageOptionsValidator();
        var options = new StorageOptions { Provider = "LocalFilesystem" };

        var result = validator.Validate(null, options);

        Assert.That(result.Failed, Is.True);
        Assert.That(result.Failures, Has.Some.Contains("RootPath"));
    }

    [Test]
    public void Validator_rejects_LocalFilesystem_with_TimeLimitedUrl_serving()
    {
        var validator = new StorageOptionsValidator();
        var options = new StorageOptions
        {
            Provider = "LocalFilesystem",
            LocalFilesystem = new StorageLocalFilesystemOptions { RootPath = "/tmp/x" },
        };
        options.Categories.SignedFundingAgreement.ServingMode = ServingMode.TimeLimitedUrl;

        var result = validator.Validate(null, options);

        Assert.That(result.Failed, Is.True);
        Assert.That(result.Failures, Has.Some.Contains("TimeLimitedUrl"));
    }

    [Test]
    public void Validator_rejects_url_expiry_over_15_minutes()
    {
        var validator = new StorageOptionsValidator();
        var options = new StorageOptions { Provider = "AzureBlob" };
        options.Categories.SignedFundingAgreement.UrlExpirySeconds = 60 * 60; // 1h

        var result = validator.Validate(null, options);

        Assert.That(result.Failed, Is.True);
        Assert.That(result.Failures, Has.Some.Contains("UrlExpirySeconds"));
    }

    [Test]
    public void Validator_rejects_zero_or_negative_max_size()
    {
        var validator = new StorageOptionsValidator();
        var options = new StorageOptions { Provider = "AzureBlob" };
        options.Categories.SignedFundingAgreement.MaxSizeBytes = 0;

        var result = validator.Validate(null, options);

        Assert.That(result.Failed, Is.True);
        Assert.That(result.Failures, Has.Some.Contains("MaxSizeBytes"));
    }

    [Test]
    public void Validator_accepts_valid_configuration()
    {
        var validator = new StorageOptionsValidator();
        var options = new StorageOptions { Provider = "AzureBlob" };

        var result = validator.Validate(null, options);

        Assert.That(result.Succeeded, Is.True, () => string.Join("; ", result.Failures ?? Array.Empty<string>()));
    }

    [Test]
    public void Binds_from_configuration()
    {
        var dict = new Dictionary<string, string?>
        {
            ["Storage:Provider"] = "AzureBlob",
            ["Storage:Categories:SignedFundingAgreement:MaxSizeBytes"] = "31457280", // 30 MiB override
            ["Storage:Categories:SignedFundingAgreement:UrlExpirySeconds"] = "300",
        };
        var config = new ConfigurationBuilder().AddInMemoryCollection(dict).Build();

        var options = new StorageOptions();
        config.GetSection(StorageOptions.SectionName).Bind(options);

        Assert.That(options.Provider, Is.EqualTo("AzureBlob"));
        Assert.That(options.Categories.SignedFundingAgreement.MaxSizeBytes, Is.EqualTo(31457280));
        Assert.That(options.Categories.SignedFundingAgreement.UrlExpirySeconds, Is.EqualTo(300));
    }
}
