// Spec 047 — see specs/047-evidence-graph-required-docs/contracts/interfaces.md (HTTP surface).

using System.Globalization;
using System.Security.Claims;
using FundingPlatform.Application.Abstractions.Storage;
using FundingPlatform.Application.Admin.Users.DTOs;
using FundingPlatform.Application.Evidence;
using FundingPlatform.Application.FundsUsageEvidence;
using FundingPlatform.Application.Reviewer;
using FundingPlatform.Domain.Enums;
using FundingPlatform.Domain.Interfaces;
using FundingPlatform.Infrastructure.Persistence;
using FundingPlatform.Web.Filters;
using FundingPlatform.Web.Resources;
using FundingPlatform.Web.ViewModels.Evidence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FundingPlatform.Web.Controllers;

/// <summary>
/// Spec 047 — the per-application evidence-graph surface, mounted per application (mirrors
/// <c>DisbursementController</c>). Only the Financial Operator writes; Auditor AND Admin get
/// read-only (write POSTs → 403), per FR-030. Every action enforces the role gate (attribute), the
/// group-overlap + <see cref="ApplicationState.AgreementExecuted"/> gate (flat 404, no disclosure),
/// and a cross-application evidence-id guard.
/// </summary>
[Authorize(Roles = "Financial Operator,Admin,Auditor")]
// Route is /EvidenceGraph (NOT /Evidence): spec 036 FundsUsageEvidenceController already owns
// /Applications/{id}/Evidence. The spec-047 evidence GRAPH is the financial-execution surface.
[Route("Applications/{applicationId:int}/EvidenceGraph")]
public sealed class EvidenceController : Controller
{
    private const string EvidenceCurrency = "CRC";

    private readonly IEvidenceService _service;
    private readonly FundingPlatform.Application.DocRules.ILineCompletenessProjection _completeness;
    private readonly IBudgetLineClosureService _closure;
    private readonly IReviewerScopeProvider _scopeProvider;
    private readonly IApplicationRepository _appRepo;
    private readonly AppDbContext _db;

    public EvidenceController(
        IEvidenceService service,
        FundingPlatform.Application.DocRules.ILineCompletenessProjection completeness,
        IBudgetLineClosureService closure,
        IReviewerScopeProvider scopeProvider,
        IApplicationRepository appRepo,
        AppDbContext db)
    {
        _service = service;
        _completeness = completeness;
        _closure = closure;
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

        var lines = await LineOptionsAsync(applicationId, ct);
        var completeness = await _completeness.GetForApplicationAsync(applicationId, ct);
        var closedItemIds = await _db.Items.AsNoTracking()
            .Where(i => i.ApplicationId == applicationId && i.ClosureState == ItemClosureState.Closed)
            .Select(i => i.Id).ToListAsync(ct);
        var closedSet = closedItemIds.ToHashSet();
        var canWrite = CanWrite();
        var completenessRows = lines
            .Where(l => completeness.ContainsKey(l.ItemId))
            .Select(l => new CompletenessRowViewModel(
                applicationId, l.Label, completeness[l.ItemId], closedSet.Contains(l.ItemId), canWrite))
            .ToList();

        return View(new EvidenceIndexViewModel
        {
            ApplicationId = applicationId,
            Items = await _service.ListForApplicationAsync(applicationId, ct),
            CanWrite = CanWrite(),
            AcceptExtensions = string.Join(",", EvidenceFileTypePolicy.AllowedExtensions),
            Lines = lines,
            Completeness = completenessRows,
        });
    }

    [HttpGet("{evidenceId:int}")]
    public async Task<IActionResult> Detail(int applicationId, int evidenceId, CancellationToken ct)
    {
        if (!await IsAccessibleAsync(applicationId, ct))
        {
            return NotFound();
        }

        var detail = await _service.GetAsync(applicationId, evidenceId, ct);
        if (detail is null)
        {
            return NotFound();
        }

        return View(new EvidenceDetailViewModel
        {
            ApplicationId = applicationId,
            Detail = detail,
            CanWrite = CanWrite(),
            AcceptExtensions = string.Join(",", EvidenceFileTypePolicy.AllowedExtensions),
            Lines = await LineOptionsAsync(applicationId, ct),
        });
    }

    [HttpPost("")]
    [ValidateAntiForgeryToken]
    [UploadSizeGuard(FileCategory.Evidence)]
    public async Task<IActionResult> Attach(
        int applicationId, IFormFile? file, string? type, int? disbursementId, string? amount,
        string? documentReferenceNumber, string? documentDate, int? supplierId,
        int[]? lineItemId, string[]? lineAmount, CancellationToken ct)
    {
        var guard = await GuardWriteAsync(applicationId, ct);
        if (guard is not null)
        {
            return guard;
        }

        if (!Enum.TryParse<EvidenceType>(type, ignoreCase: true, out var evidenceType))
        {
            TempData["ErrorMessage"] = EvidenceResources.Error_InvalidInput;
            return RedirectToIndex(applicationId);
        }
        if (file is null || file.Length == 0)
        {
            TempData["ErrorMessage"] = EvidenceResources.Error_FileRequired;
            return RedirectToIndex(applicationId);
        }

        using var buffer = new MemoryStream();
        await file.CopyToAsync(buffer, ct);
        buffer.Position = 0;
        var head = new byte[EvidenceFileTypePolicy.HeadByteCount];
        var read = await buffer.ReadAsync(head.AsMemory(0, head.Length), ct);
        buffer.Position = 0;
        if (!EvidenceFileTypePolicy.IsAllowed(file.FileName, file.ContentType, head.AsSpan(0, read)))
        {
            TempData["ErrorMessage"] = EvidenceResources.Error_FileType;
            return RedirectToIndex(applicationId);
        }

        var cmd = new AttachEvidenceCommand(
            applicationId, evidenceType, NormalizeAnchor(disbursementId), ParseAmount(amount),
            EvidenceCurrency, documentReferenceNumber ?? string.Empty, ParseDate(documentDate),
            NormalizeAnchor(supplierId), BuildLines(lineItemId, lineAmount),
            buffer, file.FileName, file.ContentType, file.Length);

        Result<int> result;
        try
        {
            result = await _service.AttachAsync(cmd, GetUserId(), ct);
        }
        catch (Exception)
        {
            TempData["ErrorMessage"] = FundingPlatform.Application.Evidence.EvidenceReasons.UploadFailed;
            return RedirectToIndex(applicationId);
        }

        return Flash(result, EvidenceResources.Flash_Attached, applicationId);
    }

    [HttpPost("{evidenceId:int}/Allocate")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Allocate(
        int applicationId, int evidenceId, int[]? lineItemId, string[]? lineAmount, CancellationToken ct)
    {
        var guard = await GuardWriteAsync(applicationId, ct);
        if (guard is not null)
        {
            return guard;
        }
        if (!await EvidenceBelongsAsync(applicationId, evidenceId, ct))
        {
            return NotFound();
        }

        var cmd = new AllocateEvidenceCommand(applicationId, evidenceId, BuildLines(lineItemId, lineAmount));
        var result = await _service.AllocateAsync(cmd, GetUserId(), ct);
        return FlashToDetail(result, EvidenceResources.Flash_Allocated, applicationId, evidenceId);
    }

    [HttpPost("{evidenceId:int}/Replace")]
    [ValidateAntiForgeryToken]
    [UploadSizeGuard(FileCategory.Evidence)]
    public async Task<IActionResult> Replace(
        int applicationId, int evidenceId, IFormFile? file, string? reason, string? amount,
        string? documentReferenceNumber, string? documentDate, CancellationToken ct)
    {
        var guard = await GuardWriteAsync(applicationId, ct);
        if (guard is not null)
        {
            return guard;
        }
        if (!await EvidenceBelongsAsync(applicationId, evidenceId, ct))
        {
            return NotFound();
        }

        Stream? content = null;
        string? fileName = null;
        string? contentType = null;
        long? fileSize = null;
        MemoryStream? buffer = null;
        try
        {
            if (file is not null && file.Length > 0)
            {
                buffer = new MemoryStream();
                await file.CopyToAsync(buffer, ct);
                buffer.Position = 0;
                var head = new byte[EvidenceFileTypePolicy.HeadByteCount];
                var read = await buffer.ReadAsync(head.AsMemory(0, head.Length), ct);
                buffer.Position = 0;
                if (!EvidenceFileTypePolicy.IsAllowed(file.FileName, file.ContentType, head.AsSpan(0, read)))
                {
                    TempData["ErrorMessage"] = EvidenceResources.Error_FileType;
                    return RedirectToDetail(applicationId, evidenceId);
                }
                content = buffer;
                fileName = file.FileName;
                contentType = file.ContentType;
                fileSize = file.Length;
            }

            var cmd = new ReplaceEvidenceCommand(
                applicationId, evidenceId, reason ?? string.Empty, ParseAmount(amount), EvidenceCurrency,
                documentReferenceNumber ?? string.Empty, ParseDate(documentDate), content, fileName, contentType, fileSize);

            Result result;
            try
            {
                result = await _service.ReplaceAsync(cmd, GetUserId(), ct);
            }
            catch (Exception)
            {
                TempData["ErrorMessage"] = FundingPlatform.Application.Evidence.EvidenceReasons.UploadFailed;
                return RedirectToDetail(applicationId, evidenceId);
            }

            return FlashToDetail(result, EvidenceResources.Flash_Replaced, applicationId, evidenceId);
        }
        finally
        {
            buffer?.Dispose();
        }
    }

    [HttpPost("{evidenceId:int}/Delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int applicationId, int evidenceId, CancellationToken ct)
    {
        var guard = await GuardWriteAsync(applicationId, ct);
        if (guard is not null)
        {
            return guard;
        }
        if (!await EvidenceBelongsAsync(applicationId, evidenceId, ct))
        {
            return NotFound();
        }

        var result = await _service.DeleteAsync(applicationId, evidenceId, GetUserId(), ct);
        return Flash(result, EvidenceResources.Flash_Deleted, applicationId);
    }

    // Spec 047 / US3 — budget-line closure (app-level route per the contract, absolute so it is not
    // nested under /Evidence). Financial Operator only; group-scope + read-only guards reused.
    [HttpPost("/Applications/{applicationId:int}/Lines/{itemId:int}/Close")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Close(int applicationId, int itemId, string? reason, CancellationToken ct)
    {
        var guard = await GuardWriteAsync(applicationId, ct);
        if (guard is not null)
        {
            return guard;
        }
        var result = await _closure.CloseAsync(applicationId, itemId, reason, GetUserId(), ct);
        return Flash(result, EvidenceResources.Closure_Flash_Closed, applicationId);
    }

    [HttpPost("/Applications/{applicationId:int}/Lines/{itemId:int}/Reopen")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reopen(int applicationId, int itemId, string? reason, CancellationToken ct)
    {
        var guard = await GuardWriteAsync(applicationId, ct);
        if (guard is not null)
        {
            return guard;
        }
        var result = await _closure.ReopenAsync(applicationId, itemId, reason ?? string.Empty, GetUserId(), ct);
        return Flash(result, EvidenceResources.Closure_Flash_Reopened, applicationId);
    }

    [HttpGet("{evidenceId:int}/Download")]
    public async Task<IActionResult> Download(int applicationId, int evidenceId, int? v, CancellationToken ct)
    {
        if (!await IsAccessibleAsync(applicationId, ct)
            || !await EvidenceBelongsAsync(applicationId, evidenceId, ct))
        {
            return NotFound();
        }

        var download = await _service.OpenForDownloadAsync(applicationId, evidenceId, v, ct);
        if (download is null)
        {
            return NotFound();
        }

        return File(download.Content, download.ContentType, fileDownloadName: download.FileName);
    }

    // ---------------------------------------------------------------------

    private async Task<IReadOnlyList<EvidenceLineOption>> LineOptionsAsync(int applicationId, CancellationToken ct)
    {
        var rows = await _db.Items.AsNoTracking()
            .Where(i => i.ApplicationId == applicationId)
            .OrderBy(i => i.LineCode).ThenBy(i => i.Id)
            .Select(i => new { i.Id, i.LineCode, i.ProductName })
            .ToListAsync(ct);
        return rows.Select(r => new EvidenceLineOption(
            r.Id,
            !string.IsNullOrWhiteSpace(r.LineCode) ? $"{r.LineCode} — {r.ProductName}" : r.ProductName)).ToList();
    }

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

    private async Task<IActionResult?> GuardWriteAsync(int applicationId, CancellationToken ct)
    {
        if (!await IsAccessibleAsync(applicationId, ct))
        {
            return NotFound();
        }
        if (!CanWrite())
        {
            return Forbid(); // in-scope Auditor/Admin: read-only (FR-030)
        }
        return null;
    }

    private async Task<bool> EvidenceBelongsAsync(int applicationId, int evidenceId, CancellationToken ct)
        => await _db.Evidence.AsNoTracking()
            .AnyAsync(e => e.Id == evidenceId && e.ApplicationId == applicationId, ct);

    // FR-030 — only the Financial Operator may write; Auditor AND Admin are read-only.
    private bool CanWrite() => User.IsInRole("Financial Operator");

    /// <summary>Zip the parallel <c>lineItemId[]</c>/<c>lineAmount[]</c> form arrays into allocations,
    /// dropping rows with a blank/zero amount.</summary>
    private static IReadOnlyList<EvidenceLineAllocationInput> BuildLines(int[]? itemIds, string[]? amounts)
    {
        var lines = new List<EvidenceLineAllocationInput>();
        if (itemIds is null || amounts is null)
        {
            return lines;
        }
        for (var i = 0; i < itemIds.Length && i < amounts.Length; i++)
        {
            var amt = ParseAmount(amounts[i]);
            if (amt > 0m)
            {
                lines.Add(new EvidenceLineAllocationInput(itemIds[i], amt));
            }
        }
        return lines;
    }

    private static int? NormalizeAnchor(int? id) => id is > 0 ? id : null;

    private IActionResult Flash(Result result, string successMessage, int applicationId)
    {
        TempData[result.Succeeded ? "SuccessMessage" : "ErrorMessage"] = result.Succeeded ? successMessage : FirstError(result);
        return RedirectToIndex(applicationId);
    }

    private IActionResult Flash(Result<int> result, string successMessage, int applicationId)
    {
        TempData[result.Succeeded ? "SuccessMessage" : "ErrorMessage"] = result.Succeeded ? successMessage : FirstError(result.Errors);
        return RedirectToIndex(applicationId);
    }

    private IActionResult FlashToDetail(Result result, string successMessage, int applicationId, int evidenceId)
    {
        TempData[result.Succeeded ? "SuccessMessage" : "ErrorMessage"] = result.Succeeded ? successMessage : FirstError(result);
        return RedirectToDetail(applicationId, evidenceId);
    }

    private IActionResult RedirectToIndex(int applicationId) => RedirectToAction(nameof(Index), new { applicationId });

    private IActionResult RedirectToDetail(int applicationId, int evidenceId)
        => RedirectToAction(nameof(Detail), new { applicationId, evidenceId });

    private static string FirstError(Result result)
        => result.Errors.Count > 0 ? result.Errors[0].Message : EvidenceResources.Error_InvalidInput;

    private static string FirstError(IReadOnlyList<DomainError> errors)
        => errors.Count > 0 ? errors[0].Message : EvidenceResources.Error_InvalidInput;

    private static decimal ParseAmount(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return 0m;
        }
        var s = raw.Trim();
        if (decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out var invariant))
        {
            return invariant;
        }
        return decimal.TryParse(s, NumberStyles.Number, new CultureInfo("es-CR"), out var local) ? local : 0m;
    }

    private static DateOnly ParseDate(string? raw)
        => DateOnly.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d
            : DateOnly.FromDateTime(DateTime.UtcNow);

    private string GetUserId() => User.FindFirstValue(ClaimTypes.NameIdentifier)!;
}
