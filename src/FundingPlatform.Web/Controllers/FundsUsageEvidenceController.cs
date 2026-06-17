// Spec 036 — see specs/036-funds-usage-evidence/contracts/ui-and-routes.md.

using System.Security.Claims;
using FundingPlatform.Application.Abstractions.Storage;
using FundingPlatform.Application.FundsUsageEvidence;
using FundingPlatform.Application.Reviewer;
using FundingPlatform.Domain.Enums;
using FundingPlatform.Domain.Interfaces;
using FundingPlatform.Infrastructure.Persistence;
using FundingPlatform.Web.Filters;
using FundingPlatform.Web.Resources;
using FundingPlatform.Web.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FundingPlatform.Web.Controllers;

/// <summary>
/// Spec 036 — post-execution reviewer stage for funds-usage evidence. Mounted per
/// application (mirrors <c>FundingAgreementController</c>). Every action enforces the
/// reviewer/admin role gate (attribute), the group-scope gate (no disclosure), and the
/// <see cref="ApplicationState.AgreementExecuted"/> gate — any failure is a flat 404.
/// </summary>
[Authorize(Roles = "Reviewer,Admin")]
[Route("Applications/{applicationId:int}/Evidence")]
public sealed class FundsUsageEvidenceController : Controller
{
    private readonly IFundsUsageEvidenceService _service;
    private readonly IReviewerScopeProvider _scopeProvider;
    private readonly IApplicationRepository _appRepo;
    private readonly AppDbContext _db;

    public FundsUsageEvidenceController(
        IFundsUsageEvidenceService service,
        IReviewerScopeProvider scopeProvider,
        IApplicationRepository appRepo,
        AppDbContext db)
    {
        _service = service;
        _scopeProvider = scopeProvider;
        _appRepo = appRepo;
        _db = db;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(int applicationId, CancellationToken ct)
    {
        if (!await IsAccessibleAsync(applicationId, ct))
        {
            return NotFound();
        }

        var items = await _service.ListAsync(applicationId, ct);
        return View(new FundsUsageEvidenceIndexViewModel
        {
            ApplicationId = applicationId,
            Items = items,
            AcceptExtensions = string.Join(",", EvidenceFileTypePolicy.AllowedExtensions),
        });
    }

    [HttpPost("Upload")]
    [ValidateAntiForgeryToken]
    [UploadSizeGuard(FileCategory.FundsUsageEvidence)]
    public async Task<IActionResult> Upload(int applicationId, IFormFile? file, string? note, CancellationToken ct)
    {
        if (!await IsAccessibleAsync(applicationId, ct))
        {
            return NotFound();
        }

        if (file is null || file.Length == 0)
        {
            TempData["ErrorMessage"] = FundsUsageEvidenceResources.Error_FileRequired;
            return RedirectToAction(nameof(Index), new { applicationId });
        }

        // Buffer the file so we can sniff the magic bytes and then re-read it for storage.
        using var buffer = new MemoryStream();
        await file.CopyToAsync(buffer, ct);
        buffer.Position = 0;

        var head = new byte[EvidenceFileTypePolicy.HeadByteCount];
        var read = await buffer.ReadAsync(head.AsMemory(0, head.Length), ct);
        buffer.Position = 0;

        if (!EvidenceFileTypePolicy.IsAllowed(file.FileName, file.ContentType, head.AsSpan(0, read)))
        {
            TempData["ErrorMessage"] = FundsUsageEvidenceResources.Error_FileType;
            return RedirectToAction(nameof(Index), new { applicationId });
        }

        try
        {
            await _service.UploadAsync(
                new UploadFundsUsageEvidenceCommand(
                    applicationId, file.FileName, file.ContentType, file.Length, buffer, note),
                GetUserId(), ct);
        }
        catch (InvalidOperationException)
        {
            // The domain factory throws for two reasons: a note over 250 chars, or the
            // application no longer being executed (a state race after the gate check).
            // Label each correctly rather than always blaming the note.
            TempData["ErrorMessage"] = (note?.Trim().Length ?? 0) > FundingPlatform.Domain.Entities.FundsUsageEvidence.MaxNoteLength
                ? FundsUsageEvidenceResources.Error_NoteTooLong
                : FundsUsageEvidenceResources.Error_UploadFailed;
            return RedirectToAction(nameof(Index), new { applicationId });
        }

        TempData["SuccessMessage"] = FundsUsageEvidenceResources.Flash_Uploaded;
        return RedirectToAction(nameof(Index), new { applicationId });
    }

    [HttpPost("{evidenceId:int}/Note")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditNote(int applicationId, int evidenceId, string? note, CancellationToken ct)
    {
        if (!await IsAccessibleAsync(applicationId, ct) || !await EvidenceBelongsAsync(applicationId, evidenceId, ct))
        {
            return NotFound();
        }

        try
        {
            await _service.EditNoteAsync(evidenceId, note, GetUserId(), ct);
        }
        catch (InvalidOperationException)
        {
            TempData["ErrorMessage"] = FundsUsageEvidenceResources.Error_NoteTooLong;
            return RedirectToAction(nameof(Index), new { applicationId });
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }

        TempData["SuccessMessage"] = FundsUsageEvidenceResources.Flash_NoteSaved;
        return RedirectToAction(nameof(Index), new { applicationId });
    }

    [HttpPost("{evidenceId:int}/Delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int applicationId, int evidenceId, CancellationToken ct)
    {
        if (!await IsAccessibleAsync(applicationId, ct) || !await EvidenceBelongsAsync(applicationId, evidenceId, ct))
        {
            return NotFound();
        }

        try
        {
            await _service.DeleteAsync(evidenceId, GetUserId(), ct);
        }
        catch (KeyNotFoundException)
        {
            // Already gone (e.g. concurrent delete) — harmless (research D9).
            return RedirectToAction(nameof(Index), new { applicationId });
        }

        TempData["SuccessMessage"] = FundsUsageEvidenceResources.Flash_Deleted;
        return RedirectToAction(nameof(Index), new { applicationId });
    }

    [HttpGet("{evidenceId:int}/Download")]
    public async Task<IActionResult> Download(int applicationId, int evidenceId, CancellationToken ct)
    {
        if (!await IsAccessibleAsync(applicationId, ct) || !await EvidenceBelongsAsync(applicationId, evidenceId, ct))
        {
            return NotFound();
        }

        var download = await _service.OpenForDownloadAsync(evidenceId, ct);
        if (download is null)
        {
            return NotFound();
        }

        return File(download.Content, download.ContentType, fileDownloadName: download.FileName);
    }

    // ---------------------------------------------------------------------

    /// <summary>
    /// FR-001/FR-002/FR-012 — the stage is reachable iff the caller is in scope
    /// (admin short-circuit, else group overlap) AND the application exists and is
    /// in <see cref="ApplicationState.AgreementExecuted"/>. Every miss is a flat 404
    /// (no disclosure).
    /// </summary>
    private async Task<bool> IsAccessibleAsync(int applicationId, CancellationToken ct)
    {
        var scope = await _scopeProvider.GetForUserAsync(GetUserId(), User.IsInRole("Admin"), ct);
        if (!scope.IsAdmin && !await _appRepo.ApplicantSharesAnyGroupAsync(applicationId, scope.GroupIds, ct))
        {
            return false;
        }

        var state = await _db.Applications.AsNoTracking()
            .Where(a => a.Id == applicationId)
            .Select(a => (ApplicationState?)a.State)
            .FirstOrDefaultAsync(ct);

        return state == ApplicationState.AgreementExecuted;
    }

    /// <summary>Guards against a route whose evidence id belongs to a different
    /// application than the (scoped) route application — closes the cross-scope hole.</summary>
    private async Task<bool> EvidenceBelongsAsync(int applicationId, int evidenceId, CancellationToken ct)
        => await _db.FundsUsageEvidence.AsNoTracking()
            .AnyAsync(e => e.Id == evidenceId && e.ApplicationId == applicationId, ct);

    private string GetUserId() => User.FindFirstValue(ClaimTypes.NameIdentifier)!;
}
