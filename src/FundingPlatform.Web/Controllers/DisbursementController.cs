// Spec 045 — see specs/045-financial-disbursement-core/contracts/interfaces.md (HTTP surface).

using System.Globalization;
using System.Security.Claims;
using FundingPlatform.Application.Abstractions.Storage;
using FundingPlatform.Application.Admin.Users.DTOs;
using FundingPlatform.Application.Disbursements;
using FundingPlatform.Application.FundsUsageEvidence;
using FundingPlatform.Application.Reviewer;
using FundingPlatform.Domain.Enums;
using FundingPlatform.Domain.Interfaces;
using FundingPlatform.Infrastructure.Persistence;
using FundingPlatform.Web.Filters;
using FundingPlatform.Web.Resources;
using FundingPlatform.Web.ViewModels.Disbursements;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FundingPlatform.Web.Controllers;

/// <summary>
/// Spec 045 — the per-application financial disbursement surface, mounted per application
/// (mirrors <c>FundsUsageEvidenceController</c>). Only the Financial Operator writes; Auditor
/// AND Admin get read-only (write POSTs → 403), per FR-025. Every action enforces the role
/// gate (attribute), the group-overlap + <see cref="ApplicationState.AgreementExecuted"/> gate
/// (flat 404, no disclosure), and a cross-application disbursement-id guard.
/// </summary>
[Authorize(Roles = "Financial Operator,Admin,Auditor")]
[Route("Applications/{applicationId:int}/Disbursements")]
public sealed class DisbursementController : Controller
{
    private readonly IDisbursementService _service;
    private readonly IParticipantBalanceProjection _balance;
    private readonly IReviewerScopeProvider _scopeProvider;
    private readonly IApplicationRepository _appRepo;
    private readonly AppDbContext _db;

    public DisbursementController(
        IDisbursementService service,
        IParticipantBalanceProjection balance,
        IReviewerScopeProvider scopeProvider,
        IApplicationRepository appRepo,
        AppDbContext db)
    {
        _service = service;
        _balance = balance;
        _scopeProvider = scopeProvider;
        _appRepo = appRepo;
        _db = db;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(
        int applicationId,
        int? trancheId, bool synthetic, string? status, int? supplierId,
        string? validationState, string? dateFrom, string? dateTo,
        CancellationToken ct)
    {
        if (!await IsAccessibleAsync(applicationId, ct))
        {
            return NotFound();
        }

        var filterForm = new BudgetLineFilterForm
        {
            TrancheId = trancheId,
            Synthetic = synthetic,
            Status = status,
            SupplierId = supplierId,
            ValidationState = validationState,
            DateFrom = ParseDateOrNull(dateFrom),
            DateTo = ParseDateOrNull(dateTo),
        };

        var items = await _service.ListAsync(applicationId, ct);
        var balance = await _balance.GetForApplicationAsync(applicationId, ct);
        var composed = await _balance.GetComposedForApplicationAsync(applicationId, BuildFilter(filterForm), ct);
        var supplierOptions = await SupplierOptionsAsync(applicationId, ct);
        var trancheOptions = await TrancheOptionsAsync(applicationId, ct);

        return View(new DisbursementIndexViewModel
        {
            ApplicationId = applicationId,
            Balance = balance,
            Items = items,
            CanWrite = CanWrite(),
            Composed = composed,
            Filter = filterForm,
            SupplierOptions = supplierOptions,
            TrancheOptions = trancheOptions,
        });
    }

    /// <summary>Translate the raw filter form into the projection's typed filter (null when no facets set).</summary>
    private static BudgetLineFilter? BuildFilter(BudgetLineFilterForm f)
    {
        if (!f.IsActive)
        {
            return null;
        }
        BudgetLineStatus? status = Enum.TryParse<BudgetLineStatus>(f.Status, ignoreCase: true, out var s) ? s : null;
        BudgetLineValidationState? vs = Enum.TryParse<BudgetLineValidationState>(f.ValidationState, ignoreCase: true, out var v) ? v : null;
        return new BudgetLineFilter(
            TrancheId: f.TrancheId,
            IncludeSyntheticTranche: f.Synthetic,
            Status: status,
            SupplierId: f.SupplierId,
            ValidationState: vs,
            PaymentDateFrom: f.DateFrom,
            PaymentDateTo: f.DateTo);
    }

    private async Task<IReadOnlyList<(int Id, string Name)>> SupplierOptionsAsync(int applicationId, CancellationToken ct)
        => await _db.Items.AsNoTracking()
            .Where(i => i.ApplicationId == applicationId && i.SelectedSupplierId != null && i.SelectedSupplier != null)
            .Select(i => new { Id = i.SelectedSupplierId!.Value, Name = i.SelectedSupplier!.Name })
            .Distinct()
            .OrderBy(x => x.Name)
            .Select(x => new ValueTuple<int, string>(x.Id, x.Name))
            .ToListAsync(ct);

    private async Task<IReadOnlyList<(int Id, string Name)>> TrancheOptionsAsync(int applicationId, CancellationToken ct)
        => await _db.Tranches.AsNoTracking()
            .Where(t => t.ApplicationId == applicationId)
            .OrderBy(t => t.Ordinal).ThenBy(t => t.Id)
            .Select(t => new ValueTuple<int, string>(t.Id, t.Name))
            .ToListAsync(ct);

    private static DateOnly? ParseDateOrNull(string? raw)
        => DateOnly.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;

    [HttpGet("{disbursementId:int}")]
    public async Task<IActionResult> Detail(int applicationId, int disbursementId, CancellationToken ct)
    {
        // Layered defense: the cross-application id guard mirrors every per-disbursement action
        // (the service already filters by ApplicationId, but this keeps Detail consistent).
        if (!await IsAccessibleAsync(applicationId, ct)
            || !await DisbursementBelongsAsync(applicationId, disbursementId, ct))
        {
            return NotFound();
        }

        var detail = await _service.GetAsync(applicationId, disbursementId, ct);
        if (detail is null)
        {
            return NotFound();
        }

        return View(new DisbursementDetailViewModel
        {
            ApplicationId = applicationId,
            Detail = detail,
            CanWrite = CanWrite(),
            AcceptExtensions = string.Join(",", EvidenceFileTypePolicy.AllowedExtensions),
        });
    }

    [HttpPost("Record")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Record(
        int applicationId, string? paymentDate, string? amount,
        string? bankTransactionReference, string? bankAccountReference, CancellationToken ct)
    {
        var guard = await GuardWriteAsync(applicationId, ct);
        if (guard is not null)
        {
            return guard;
        }

        var cmd = new RecordDisbursementCommand(
            applicationId, ParseDate(paymentDate), ParseAmount(amount),
            bankTransactionReference ?? string.Empty, bankAccountReference);

        var result = await _service.RecordAsync(cmd, GetUserId(), ct);
        return Flash(result, DisbursementResources.Flash_Recorded, applicationId);
    }

    [HttpPost("{disbursementId:int}/Edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(
        int applicationId, int disbursementId, string? paymentDate, string? amount,
        string? bankTransactionReference, string? bankAccountReference, CancellationToken ct)
    {
        var guard = await GuardWriteAsync(applicationId, ct);
        if (guard is not null)
        {
            return guard;
        }
        if (!await DisbursementBelongsAsync(applicationId, disbursementId, ct))
        {
            return NotFound();
        }

        var cmd = new EditDisbursementCommand(
            applicationId, disbursementId, ParseDate(paymentDate), ParseAmount(amount),
            bankTransactionReference ?? string.Empty, bankAccountReference);

        var result = await _service.EditAsync(cmd, GetUserId(), ct);
        return FlashToDetail(result, DisbursementResources.Flash_Edited, applicationId, disbursementId);
    }

    [HttpPost("{disbursementId:int}/Evidence")]
    [ValidateAntiForgeryToken]
    [UploadSizeGuard(FileCategory.DisbursementEvidence)]
    public async Task<IActionResult> Evidence(
        int applicationId, int disbursementId, IFormFile? file, string? kind, string? amount,
        string? documentReferenceNumber, string? documentDate, CancellationToken ct)
    {
        var guard = await GuardWriteAsync(applicationId, ct);
        if (guard is not null)
        {
            return guard;
        }
        if (!await DisbursementBelongsAsync(applicationId, disbursementId, ct))
        {
            return NotFound();
        }

        if (!Enum.TryParse<EvidenceKind>(kind, ignoreCase: true, out var evidenceKind))
        {
            TempData["ErrorMessage"] = DisbursementResources.Error_InvalidInput;
            return RedirectToDetail(applicationId, disbursementId);
        }
        if (file is null || file.Length == 0)
        {
            TempData["ErrorMessage"] = DisbursementResources.Error_FileRequired;
            return RedirectToDetail(applicationId, disbursementId);
        }

        // Buffer the file to sniff the magic bytes, then re-read it for storage.
        using var buffer = new MemoryStream();
        await file.CopyToAsync(buffer, ct);
        buffer.Position = 0;

        var head = new byte[EvidenceFileTypePolicy.HeadByteCount];
        var read = await buffer.ReadAsync(head.AsMemory(0, head.Length), ct);
        buffer.Position = 0;

        if (!EvidenceFileTypePolicy.IsAllowed(file.FileName, file.ContentType, head.AsSpan(0, read)))
        {
            TempData["ErrorMessage"] = DisbursementResources.Error_FileType;
            return RedirectToDetail(applicationId, disbursementId);
        }

        var cmd = new AttachDisbursementEvidenceCommand(
            applicationId, disbursementId, evidenceKind, ParseAmount(amount),
            DisbursementEvidenceCurrency, documentReferenceNumber ?? string.Empty,
            ParseDate(documentDate), buffer, file.FileName, file.ContentType, file.Length);

        Result<int> result;
        try
        {
            result = await _service.AttachEvidenceAsync(cmd, GetUserId(), ct);
        }
        catch (Exception)
        {
            TempData["ErrorMessage"] = FundingPlatform.Application.Disbursements.DisbursementReasons.EvidenceFailed;
            return RedirectToDetail(applicationId, disbursementId);
        }

        return FlashToDetail(result, DisbursementResources.Flash_EvidenceSaved, applicationId, disbursementId);
    }

    [HttpPost("{disbursementId:int}/Validate")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Validate(int applicationId, int disbursementId, CancellationToken ct)
    {
        var guard = await GuardWriteAsync(applicationId, ct);
        if (guard is not null)
        {
            return guard;
        }
        if (!await DisbursementBelongsAsync(applicationId, disbursementId, ct))
        {
            return NotFound();
        }

        var result = await _service.ValidateAsync(applicationId, disbursementId, GetUserId(), ct);
        return FlashToDetail(result, DisbursementResources.Flash_Validated, applicationId, disbursementId);
    }

    [HttpPost("{disbursementId:int}/Cancel")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(int applicationId, int disbursementId, CancellationToken ct)
    {
        var guard = await GuardWriteAsync(applicationId, ct);
        if (guard is not null)
        {
            return guard;
        }
        if (!await DisbursementBelongsAsync(applicationId, disbursementId, ct))
        {
            return NotFound();
        }

        var result = await _service.CancelAsync(applicationId, disbursementId, GetUserId(), ct);
        // Cancel returns to the list (the disbursement is terminal).
        return Flash(result, DisbursementResources.Flash_Cancelled, applicationId);
    }

    // Spec 046 / US2 — per-line commit / un-commit (Financial Operator only).

    [HttpPost("Lines/{itemId:int}/Commit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Commit(int applicationId, int itemId, CancellationToken ct)
    {
        var guard = await GuardWriteAsync(applicationId, ct);
        if (guard is not null)
        {
            return guard;
        }
        var result = await _service.CommitLineAsync(applicationId, itemId, GetUserId(), ct);
        return Flash(result, DisbursementResources.Flash_LineCommitted, applicationId);
    }

    [HttpPost("Lines/{itemId:int}/Uncommit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Uncommit(int applicationId, int itemId, CancellationToken ct)
    {
        var guard = await GuardWriteAsync(applicationId, ct);
        if (guard is not null)
        {
            return guard;
        }
        var result = await _service.UncommitLineAsync(applicationId, itemId, GetUserId(), ct);
        return Flash(result, DisbursementResources.Flash_LineUncommitted, applicationId);
    }

    [HttpGet("{disbursementId:int}/Evidence/{kind}/Download")]
    public async Task<IActionResult> Download(int applicationId, int disbursementId, string kind, CancellationToken ct)
    {
        if (!await IsAccessibleAsync(applicationId, ct)
            || !await DisbursementBelongsAsync(applicationId, disbursementId, ct)
            || !Enum.TryParse<EvidenceKind>(kind, ignoreCase: true, out var evidenceKind))
        {
            return NotFound();
        }

        var download = await _service.OpenEvidenceForDownloadAsync(applicationId, disbursementId, evidenceKind, ct);
        if (download is null)
        {
            return NotFound();
        }

        return File(download.Content, download.ContentType, fileDownloadName: download.FileName);
    }

    // ---------------------------------------------------------------------

    private const string DisbursementEvidenceCurrency = "CRC";

    /// <summary>
    /// The surface is reachable iff the caller is in scope (admin short-circuit, else group
    /// overlap) AND the application is <see cref="ApplicationState.AgreementExecuted"/>.
    /// Every miss is a flat 404 (no disclosure), mirroring <c>FundsUsageEvidenceController</c>.
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

    /// <summary>Write authorization for every mutating POST. Order matters: the access gate
    /// runs first (out-of-group/not-executed → flat 404, no disclosure), THEN the read-only
    /// write-guard (an in-scope Auditor/Admin → 403). Returns null when the caller may write.</summary>
    private async Task<IActionResult?> GuardWriteAsync(int applicationId, CancellationToken ct)
    {
        if (!await IsAccessibleAsync(applicationId, ct))
        {
            return NotFound();
        }
        if (!CanWrite())
        {
            return Forbid(); // in-scope Auditor/Admin: read-only (FR-025)
        }
        return null;
    }

    /// <summary>Cross-application guard: the disbursement id must belong to the route application.</summary>
    private async Task<bool> DisbursementBelongsAsync(int applicationId, int disbursementId, CancellationToken ct)
        => await _db.Disbursements.AsNoTracking()
            .AnyAsync(d => d.Id == disbursementId && d.ApplicationId == applicationId, ct);

    // Spec 045 / FR-025 — only the Financial Operator may write; Auditor AND Admin are
    // read-only on the financial surface (money movement is the operator's segregated duty).
    // Note: this narrows the plan's R10 "Financial Operator, Admin" write set to match the
    // explicit spec requirement (see REVIEW-CODE.md).
    private bool CanWrite() => User.IsInRole("Financial Operator");

    private IActionResult Flash(Result result, string successMessage, int applicationId)
    {
        if (result.Succeeded)
        {
            TempData["SuccessMessage"] = successMessage;
        }
        else
        {
            TempData["ErrorMessage"] = FirstError(result);
        }
        return RedirectToAction(nameof(Index), new { applicationId });
    }

    private IActionResult FlashToDetail(Result result, string successMessage, int applicationId, int disbursementId)
    {
        if (result.Succeeded)
        {
            TempData["SuccessMessage"] = successMessage;
        }
        else
        {
            TempData["ErrorMessage"] = FirstError(result);
        }
        return RedirectToDetail(applicationId, disbursementId);
    }

    private IActionResult RedirectToDetail(int applicationId, int disbursementId)
        => RedirectToAction(nameof(Detail), new { applicationId, disbursementId });

    private static string FirstError(Result result)
        => result.Errors.Count > 0 ? result.Errors[0].Message : DisbursementResources.Error_InvalidInput;

    private static decimal ParseAmount(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return 0m;
        }
        var s = raw.Trim();
        // Prefer invariant (browser number inputs post "1234.56"); fall back to es-CR.
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
