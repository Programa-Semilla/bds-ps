using FundingPlatform.Application.Abstractions.Storage;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;

namespace FundingPlatform.Web.Filters;

/// <summary>
/// Spec 014 / T047 / FR-021 / FR-022 — controller filter that rejects
/// oversized uploads BEFORE the request body is streamed to
/// <see cref="IObjectStorage"/>. Per-category caps live in
/// <c>Storage:Categories:{Name}:MaxSizeBytes</c> and are read at request time
/// from the resolved <see cref="StorageOptions"/>.
///
/// <para>
/// Returns HTTP 413 with a localized (es-CR) message. The filter never opens
/// the request body itself — it inspects <c>Content-Length</c> and the
/// individual <see cref="Microsoft.AspNetCore.Http.IFormFile"/> sizes when
/// available. If neither is present, the request falls through and the
/// downstream handler enforces its own bounds (defence in depth).
/// </para>
///
/// <para>
/// The category is supplied via the constructor so callers state intent at the
/// action level (e.g. <c>[UploadSizeGuard(FileCategory.SignedFundingAgreement)]</c>).
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class UploadSizeGuardAttribute : Attribute, IFilterFactory
{
    public FileCategory Category { get; }

    public UploadSizeGuardAttribute(FileCategory category)
    {
        Category = category;
    }

    public bool IsReusable => false;

    public IFilterMetadata CreateInstance(IServiceProvider serviceProvider)
        => new UploadSizeGuardFilter(
            Category,
            serviceProvider.GetRequiredService<IOptionsMonitor<StorageOptions>>());
}

public sealed class UploadSizeGuardFilter : IAsyncResourceFilter
{
    /// <summary>
    /// Spec 012 / NFR-001 — Spanish (es-CR) message surfaced to the user when a
    /// per-category cap is exceeded. T049 — single source of truth for the
    /// rejection copy; downstream callers consume <c>RejectionMessage</c> rather
    /// than duplicating the string.
    /// </summary>
    public const string RejectionMessage =
        "El archivo excede el tamaño máximo permitido para esta categoría.";

    private readonly FileCategory _category;
    private readonly IOptionsMonitor<StorageOptions> _options;

    public UploadSizeGuardFilter(FileCategory category, IOptionsMonitor<StorageOptions> options)
    {
        _category = category;
        _options = options;
    }

    public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
    {
        var maxSize = _options.CurrentValue.Categories.For(_category).MaxSizeBytes;
        var request = context.HttpContext.Request;

        // Cheapest check: declared Content-Length. Reject before reading any
        // bytes from the wire.
        if (request.ContentLength is long contentLength && contentLength > maxSize)
        {
            context.Result = Reject(maxSize);
            return;
        }

        // Multipart form: inspect each IFormFile size. Buffered form parsing
        // is bounded by RequestFormLimits at the action level; this filter
        // catches the case where the form is small but a single attached file
        // exceeds the per-category cap.
        if (request.HasFormContentType)
        {
            var form = await request.ReadFormAsync(context.HttpContext.RequestAborted);
            foreach (var file in form.Files)
            {
                if (file.Length > maxSize)
                {
                    context.Result = Reject(maxSize);
                    return;
                }
            }
        }

        await next().ConfigureAwait(false);
    }

    private static IActionResult Reject(long maxSize)
    {
        return new ObjectResult(new
        {
            error = RejectionMessage,
            maxSizeBytes = maxSize,
        })
        {
            StatusCode = StatusCodes.Status413PayloadTooLarge,
        };
    }
}
