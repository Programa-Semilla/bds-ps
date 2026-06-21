using System.Security.Claims;
using FundingPlatform.Application.Reviewer;
using FundingPlatform.Application.Services;
using FundingPlatform.Domain.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FundingPlatform.Web.Controllers;

/// <summary>
/// Spec 040 / FR-007 — supporting-document downloads shared by the reviewer and the
/// auditor surfaces. The quotation-PDF download and the AI-comparison citation resolver
/// were lifted out of <see cref="ReviewController"/> (routes unchanged) so the
/// <c>Auditor</c> role can reach them with read access; the same spec-016 group-overlap
/// guard applies, so an auditor can only download documents on applications whose
/// applicant shares one of their groups (admins are exempt).
/// </summary>
[Authorize(Roles = "Reviewer,Admin,Auditor")]
public sealed class ReviewDocumentsController : Controller
{
    private readonly ReviewService _reviewService;
    private readonly IReviewerScopeProvider _scopeProvider;
    private readonly IApplicationRepository _applicationRepository;

    public ReviewDocumentsController(
        ReviewService reviewService,
        IReviewerScopeProvider scopeProvider,
        IApplicationRepository applicationRepository)
    {
        _reviewService = reviewService;
        _scopeProvider = scopeProvider;
        _applicationRepository = applicationRepository;
    }

    private string GetUserId() => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    private Task<IReviewerScope> GetScopeAsync(CancellationToken ct) =>
        _scopeProvider.GetForUserAsync(GetUserId(), User.IsInRole("Admin"), ct);

    /// <summary>
    /// Spec 020 / US5 — resolve a citation source-ref into a signed URL.
    /// Citation IDs are `<applicationItemId>:<documentId>` (the orchestrator
    /// projects supplier blobs through the Document id; we resolve back here).
    /// </summary>
    [HttpGet]
    [Route("Review/Citations/{applicationItemId:int}/{sourceRefId}")]
    public async Task<IActionResult> Citation(
        int applicationItemId,
        string sourceRefId,
        CancellationToken ct)
    {
        var parentApplicationId = await _reviewService.GetApplicationIdForItemAsync(applicationItemId, ct);
        if (parentApplicationId is null) return NotFound();

        var scope = await GetScopeAsync(ct);
        if (!scope.IsAdmin)
        {
            var allowed = await _applicationRepository.ApplicantSharesAnyGroupAsync(parentApplicationId.Value, scope.GroupIds, ct);
            if (!allowed) return Forbid();
        }

        // The citation marker is rendered as a relative link to the Document by
        // id; storage handle resolution is delegated to the existing document
        // download endpoint. We 302 to that path so the spec-014 SAS-TTL policy
        // is enforced centrally.
        if (!int.TryParse(sourceRefId, out var documentId))
            return NotFound();

        // Look up the blob key via the Document row and stream it through
        // IObjectStorage (spec 014 / FR-018). The orchestrator wires storage
        // by category; supplier-quotation files live under application-attachments.
        var storage = HttpContext.RequestServices.GetRequiredService<
            FundingPlatform.Application.Abstractions.Storage.IObjectStorage>();
        var db = HttpContext.RequestServices.GetRequiredService<
            FundingPlatform.Infrastructure.Persistence.AppDbContext>();
        var doc = await db.Documents.FirstOrDefaultAsync(d => d.Id == documentId, ct);
        if (doc is null || string.IsNullOrEmpty(doc.BlobKey)) return NotFound();

        var key = FundingPlatform.Application.Abstractions.Storage.ObjectKey.Parse(doc.BlobKey);
        var handle = await storage.ResolveServingHandleAsync(
            FundingPlatform.Application.Abstractions.Storage.FileCategory.ApplicationAttachment,
            key,
            FundingPlatform.Application.Abstractions.Storage.ServingMode.TimeLimitedUrl,
            ct);

        if (handle is FundingPlatform.Application.Abstractions.Storage.TimeLimitedUrlHandle url)
            return Redirect(url.Url.ToString());
        if (handle is FundingPlatform.Application.Abstractions.Storage.BackendStreamHandle stream)
            return File(stream.Content, stream.ContentType ?? "application/octet-stream", doc.OriginalFileName);
        return NotFound();
    }

    /// <summary>
    /// Spec 023 / FR-014 (evolution 2026-05-20) — reviewer/auditor (group-scoped)
    /// and Admin download the PDF attached to any quotation on an Application
    /// they're authorized to view. Mirrors the auth + storage rails of the
    /// spec-020 <see cref="Citation"/> endpoint but is keyed by
    /// <c>quotationId</c> directly so the Review/Audit screens can build
    /// the link without an extra DocumentId resolution step.
    /// </summary>
    [HttpGet]
    [Route("Review/Quotation/{quotationId:int}/Download")]
    public async Task<IActionResult> DownloadQuotation(
        int quotationId,
        CancellationToken ct)
    {
        var db = HttpContext.RequestServices.GetRequiredService<
            FundingPlatform.Infrastructure.Persistence.AppDbContext>();
        var quotation = await db.Quotations
            .Include(q => q.Document)
            .FirstOrDefaultAsync(q => q.Id == quotationId, ct);
        if (quotation is null
            || quotation.Document is null
            || string.IsNullOrEmpty(quotation.Document.BlobKey))
            return NotFound();

        var parentApplicationId = await db.Items
            .Where(i => i.Id == quotation.ItemId)
            .Select(i => (int?)i.ApplicationId)
            .FirstOrDefaultAsync(ct);
        if (parentApplicationId is null) return NotFound();
        var scope = await GetScopeAsync(ct);
        if (!scope.IsAdmin)
        {
            var allowed = await _applicationRepository.ApplicantSharesAnyGroupAsync(
                parentApplicationId.Value, scope.GroupIds, ct);
            if (!allowed) return Forbid();
        }

        // Spec 023 / FR-014 (evolution) — same rationale as the applicant
        // download path: force BackendStream so `Content-Disposition: attachment`
        // is set on the response and the browser saves the file. Inline preview
        // is intentionally not exposed on this endpoint.
        var storage = HttpContext.RequestServices.GetRequiredService<
            FundingPlatform.Application.Abstractions.Storage.IObjectStorage>();
        var key = FundingPlatform.Application.Abstractions.Storage.ObjectKey.Parse(
            quotation.Document.BlobKey);
        var handle = await storage.ResolveServingHandleAsync(
            FundingPlatform.Application.Abstractions.Storage.FileCategory.ApplicationAttachment,
            key,
            FundingPlatform.Application.Abstractions.Storage.ServingMode.BackendStream,
            ct);

        if (handle is FundingPlatform.Application.Abstractions.Storage.BackendStreamHandle stream)
            return File(
                stream.Content,
                stream.ContentType ?? "application/octet-stream",
                quotation.Document.OriginalFileName);
        return NotFound();
    }
}
