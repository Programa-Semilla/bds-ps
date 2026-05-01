using FundingPlatform.Application.Abstractions.Storage;
using FundingPlatform.Web.Filters;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;

namespace FundingPlatform.Tests.Integration.Storage;

/// <summary>
/// Spec 014 / T051 / US5 — exercises <see cref="UploadSizeGuardAttribute"/>
/// across all four <see cref="FileCategory"/> values to confirm per-category
/// caps are enforced uniformly. The filter rejects on Content-Length BEFORE
/// the body is streamed, so the assertions don't need a live web pipeline —
/// we drive the filter directly with a stubbed <see cref="ResourceExecutingContext"/>.
/// </summary>
[TestFixture]
public class PerCategoryOversizeTests
{
    private static IEnumerable<TestCaseData> AllCategories()
    {
        yield return new TestCaseData(FileCategory.SignedFundingAgreement);
        yield return new TestCaseData(FileCategory.SupplierCatalogImport);
        yield return new TestCaseData(FileCategory.ApplicationAttachment);
        yield return new TestCaseData(FileCategory.GeneratedArtifact);
    }

    [TestCaseSource(nameof(AllCategories))]
    public async Task Oversize_request_returns_413_for_every_category(FileCategory category)
    {
        var options = new StorageOptions();
        var maxSize = options.Categories.For(category).MaxSizeBytes;

        var filter = new UploadSizeGuardFilter(category, new FixedOptionsMonitor(options));

        var ctx = new DefaultHttpContext();
        ctx.Request.ContentLength = maxSize + 1;

        var executing = new ResourceExecutingContext(
            new ActionContext(ctx, new RouteData(), new Microsoft.AspNetCore.Mvc.Abstractions.ActionDescriptor()),
            new List<IFilterMetadata>(),
            new List<Microsoft.AspNetCore.Mvc.ModelBinding.IValueProviderFactory>());

        await filter.OnResourceExecutionAsync(executing, () => throw new InvalidOperationException("Pipeline must not continue when the request is rejected."));

        Assert.That(executing.Result, Is.Not.Null);
        var objectResult = executing.Result as ObjectResult;
        Assert.That(objectResult, Is.Not.Null);
        Assert.That(objectResult!.StatusCode, Is.EqualTo(StatusCodes.Status413PayloadTooLarge));
    }

    [TestCaseSource(nameof(AllCategories))]
    public async Task At_cap_request_passes_through_for_every_category(FileCategory category)
    {
        var options = new StorageOptions();
        var maxSize = options.Categories.For(category).MaxSizeBytes;

        var filter = new UploadSizeGuardFilter(category, new FixedOptionsMonitor(options));

        var ctx = new DefaultHttpContext();
        ctx.Request.ContentLength = maxSize;

        var executing = new ResourceExecutingContext(
            new ActionContext(ctx, new RouteData(), new Microsoft.AspNetCore.Mvc.Abstractions.ActionDescriptor()),
            new List<IFilterMetadata>(),
            new List<Microsoft.AspNetCore.Mvc.ModelBinding.IValueProviderFactory>());

        var continued = false;
        await filter.OnResourceExecutionAsync(executing, () =>
        {
            continued = true;
            return Task.FromResult<ResourceExecutedContext>(new ResourceExecutedContext(executing, executing.Filters));
        });

        Assert.That(continued, Is.True, "Request at the per-category cap must pass the filter.");
        Assert.That(executing.Result, Is.Null);
    }

    [Test]
    public void Rejection_message_is_in_es_CR()
    {
        // Sentinel for FR-014 / NFR-001 — the user-facing string must not
        // regress to English copy on this code path.
        Assert.That(UploadSizeGuardFilter.RejectionMessage, Does.Contain("excede"));
        Assert.That(UploadSizeGuardFilter.RejectionMessage, Does.Contain("máximo"));
    }

    private sealed class FixedOptionsMonitor : IOptionsMonitor<StorageOptions>
    {
        private readonly StorageOptions _value;
        public FixedOptionsMonitor(StorageOptions value) => _value = value;
        public StorageOptions CurrentValue => _value;
        public StorageOptions Get(string? name) => _value;
        public IDisposable? OnChange(Action<StorageOptions, string?> listener) => null;
    }
}
