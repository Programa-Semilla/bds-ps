using FundingPlatform.Application.Notifications;
using FundingPlatform.Application.Notifications.Templates;
using FundingPlatform.Domain.Notifications;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;

namespace FundingPlatform.Web.Services;

/// <summary>
/// Spec 021 / T023 / FR-023 — Razor-backed implementation of
/// <see cref="IEmailTemplateRenderer"/>. Renders every variant's HTML body
/// (<c>Views/Emails/{ViewName}.cshtml</c>) AND plain-text fallback
/// (<c>Views/Emails/{ViewName}.text.cshtml</c>) under the shared
/// <c>_EmailLayout.cshtml</c> layout. Throws <see cref="EmailRenderException"/>
/// on render failure so the worker can map to PermanentFailure (FR-022).
///
/// <para>
/// The renderer is BackgroundService-safe: it constructs a fresh
/// <see cref="DefaultHttpContext"/> per call (no ambient HTTP request needed).
/// Mirrors the off-thread pattern in <see cref="RazorFundingAgreementHtmlRenderer"/>.
/// </para>
/// </summary>
public sealed class RazorEmailRenderer : IEmailTemplateRenderer
{
    private readonly IRazorViewEngine _viewEngine;
    private readonly ITempDataProvider _tempDataProvider;
    private readonly IServiceProvider _serviceProvider;
    private readonly IConfiguration _config;

    public RazorEmailRenderer(
        IRazorViewEngine viewEngine,
        ITempDataProvider tempDataProvider,
        IServiceProvider serviceProvider,
        IConfiguration config)
    {
        _viewEngine = viewEngine;
        _tempDataProvider = tempDataProvider;
        _serviceProvider = serviceProvider;
        _config = config;
    }

    public async Task<RenderedEmail> RenderAsync(
        NotificationEvent eventType,
        NotificationRecipient recipient,
        NotificationPayload payload,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(recipient);
        ArgumentNullException.ThrowIfNull(payload);

        var binding = NotificationTemplateBindings.For(eventType);
        var subject = NotificationTemplateBindings.RenderSubject(
            eventType, payload.ApplicantDisplayName, payload.ApplicationId);

        // FR-026 — composed deep link from Notifications:BaseUrl + role-specific route.
        var baseUrl = _config["Notifications:BaseUrl"] ?? string.Empty;
        var ctaUrl = ComposeCtaUrl(eventType, recipient.Bucket, baseUrl, payload.ApplicationId);

        var senderName = _config["Notifications:Sender:Name"]
            ?? "Programa Semilla / Sistema de Banca para el Desarrollo";
        var senderEmail = _config["Notifications:Sender:Email"] ?? string.Empty;

        var model = new EmailRenderModel(
            EventType: eventType,
            Recipient: recipient,
            Payload: payload,
            Subject: subject,
            CtaUrl: ctaUrl,
            SenderName: senderName,
            SenderEmail: senderEmail);

        string htmlBody;
        string textBody;
        try
        {
            htmlBody = await RenderViewAsync($"~/Views/Emails/{binding.HtmlViewName}.cshtml", model);
            textBody = await RenderViewAsync($"~/Views/Emails/{binding.TextViewName}.cshtml", model,
                disableLayout: true);
        }
        catch (Exception ex) when (ex is not EmailRenderException)
        {
            throw new EmailRenderException(
                $"Render failed for {eventType.ToStorageString()} ({binding.HtmlViewName}/{binding.TextViewName}): {ex.Message}",
                ex);
        }

        return new RenderedEmail(subject, htmlBody, textBody);
    }

    private async Task<string> RenderViewAsync(string viewPath, EmailRenderModel model, bool disableLayout = false)
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
        var viewDictionary = new ViewDataDictionary<EmailRenderModel>(
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
        return writer.ToString();
    }

    /// <summary>
    /// Spec 021 / FR-026 / FR-040 — composes the CTA deep link.
    /// Reviewer/admin → <c>/Review/{id}</c>; applicant → <c>/Application/Details/{id}</c>.
    /// The withdrawal variant is special-cased to the reviewer queue (<c>/Review</c>)
    /// because the Application is soft-deleted and <c>/Review/{id}</c> would 403/404.
    /// </summary>
    public static string ComposeCtaUrl(
        NotificationEvent eventType, RecipientBucket bucket, string baseUrl, int applicationId)
    {
        if (eventType == NotificationEvent.WithdrawnByApplicant)
        {
            return Combine(baseUrl, "/Review");
        }

        return bucket == RecipientBucket.Applicant
            ? Combine(baseUrl, $"/Application/Details/{applicationId}")
            : Combine(baseUrl, $"/Review/{applicationId}");
    }

    private static string Combine(string baseUrl, string path)
    {
        if (string.IsNullOrEmpty(baseUrl)) return path;
        return baseUrl.TrimEnd('/') + "/" + path.TrimStart('/');
    }
}

/// <summary>
/// Spec 021 / T024 — model exposed to every email Razor view. Carries
/// everything the layout, body, and support footer need to render without
/// re-reading config or re-doing string composition inside the view.
/// </summary>
public sealed record EmailRenderModel(
    NotificationEvent EventType,
    NotificationRecipient Recipient,
    NotificationPayload Payload,
    string Subject,
    string CtaUrl,
    string SenderName,
    string SenderEmail);
