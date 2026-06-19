using FundingPlatform.Application.Notifications;
using FundingPlatform.Application.Notifications.Email;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;

namespace FundingPlatform.Web.Services;

/// <summary>
/// Spec 041 / Decision 1 / T003 — the single Razor-view-to-string primitive used
/// by every email render path (outbox <see cref="RazorEmailRenderer"/> and the
/// direct-send identity / stage / supplier / company factories). Generalized out
/// of the private method that previously lived in <c>RazorEmailRenderer</c>.
///
/// <para>BackgroundService-safe: constructs a fresh <see cref="DefaultHttpContext"/>
/// per call (no ambient HTTP request needed), mirroring the off-thread pattern in
/// <c>RazorFundingAgreementHtmlRenderer</c>. The model is passed as <see cref="object"/>
/// (assigned into a <c>ViewDataDictionary&lt;object&gt;</c>) so the same renderer
/// serves the outbox <c>EmailRenderModel</c> and the direct-send
/// <see cref="DirectEmailModel"/> alike.</para>
/// </summary>
public sealed class EmailViewRenderer : IEmailViewRenderer
{
    private readonly IRazorViewEngine _viewEngine;
    private readonly ITempDataProvider _tempDataProvider;
    private readonly IServiceProvider _serviceProvider;

    public EmailViewRenderer(
        IRazorViewEngine viewEngine,
        ITempDataProvider tempDataProvider,
        IServiceProvider serviceProvider)
    {
        _viewEngine = viewEngine;
        _tempDataProvider = tempDataProvider;
        _serviceProvider = serviceProvider;
    }

    public async Task<string> RenderViewAsync(
        string viewPath, object model, bool disableLayout, CancellationToken ct)
    {
        var httpContext = new DefaultHttpContext { RequestServices = _serviceProvider };
        var routeData = new RouteData();
        routeData.Values["controller"] = "Emails";

        var actionContext = new ActionContext(httpContext, routeData, new ActionDescriptor());

        var viewResult = _viewEngine.GetView(executingFilePath: null, viewPath, isMainPage: true);
        if (!viewResult.Success)
        {
            var locations = string.Join("\n", viewResult.SearchedLocations ?? Array.Empty<string>());
            throw new EmailRenderException(
                $"Razor view '{viewPath}' not found. Searched:\n{locations}");
        }

        await using var writer = new StringWriter();
        var viewDictionary = new ViewDataDictionary<object>(
            new EmptyModelMetadataProvider(),
            new ModelStateDictionary())
        {
            Model = model,
        };
        if (disableLayout)
        {
            // Plain-text variants don't use the shared HTML layout.
            viewDictionary["DisableLayout"] = true;
        }

        var viewContext = new ViewContext(
            actionContext,
            viewResult.View,
            viewDictionary,
            new TempDataDictionary(httpContext, _tempDataProvider),
            writer,
            new HtmlHelperOptions());

        await viewResult.View.RenderAsync(viewContext);
        var rendered = writer.ToString();

        // Spec 041 — plain-text twins (.text.cshtml) are authored with @-expressions
        // (e.g. @EmailBrand.SupportPhone, accented es-CR copy). Razor HTML-encodes
        // every @-expression, so a text body would otherwise carry "&#x2B;506" /
        // "autom&#xE1;tico". The text part is NOT HTML, so decode entities back to
        // literal characters. Scoped to the layout-less (text) render path only;
        // HTML bodies keep their entities (they render correctly in mail clients).
        return disableLayout ? System.Net.WebUtility.HtmlDecode(rendered) : rendered;
    }
}
