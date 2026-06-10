// Spec 029 — see specs/029-fund-entity/contracts/ui-and-routes.md (Admin Fund management)
// and research D7. Mirrors AdminProcessesController / AdminPublicLandingFilesController.

using FundingPlatform.Application.Abstractions.Storage;
using FundingPlatform.Application.Funds;
using FundingPlatform.Domain.Entities;
using FundingPlatform.Domain.Enums;
using FundingPlatform.Web.Filters;
using FundingPlatform.Web.Resources;
using FundingPlatform.Web.ViewModels.Admin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace FundingPlatform.Web.Controllers.Admin;

/// <summary>
/// Spec 029 / US1 — admin lifecycle for the <see cref="Fund"/> aggregate
/// (<c>/Admin/Funds</c>). Create/Edit/Archive/Reactivate plus regulation
/// upload/replace/remove. Authorization mirrors the rest of the admin sweep:
/// Admin only (SupplierAdmin denied). Regulation uploads are bounded by
/// <see cref="UploadSizeGuardAttribute"/> and validated for the PDF magic bytes
/// at this boundary; the service streams the blob through <c>IObjectStorage</c>.
/// </summary>
[Authorize(Roles = "Admin,SupplierAdmin")]
[SupplierAdminDenied]
[Route("Admin/Funds")]
public class AdminFundsController : Controller
{
    private static readonly byte[] PdfMagic = "%PDF-"u8.ToArray();

    private readonly IFundService _funds;
    private readonly UserManager<ApplicationUser> _userManager;

    public AdminFundsController(IFundService funds, UserManager<ApplicationUser> userManager)
    {
        _funds = funds;
        _userManager = userManager;
    }

    private string ActorId => _userManager.GetUserId(User) ?? string.Empty;

    [HttpGet("")]
    public async Task<IActionResult> Index(FundStatus? status, CancellationToken ct)
    {
        var rows = await _funds.ListAsync(status, ct);
        return View(new AdminFundsIndexViewModel { Rows = rows, StatusFilter = status });
    }

    [HttpGet("Create")]
    public IActionResult Create() => View(new AdminFundCreateViewModel());

    [HttpPost("Create")]
    [ValidateAntiForgeryToken]
    [UploadSizeGuard(FileCategory.FundRegulation)]
    public async Task<IActionResult> Create(AdminFundCreateViewModel vm, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            return View(vm);
        }

        FundRegulationUpload? regulation = null;
        if (vm.RegulationFile is not null && vm.RegulationFile.Length > 0)
        {
            var buffered = await ValidatePdfAsync(vm.RegulationFile, nameof(vm.RegulationFile), ct);
            if (buffered is null)
            {
                return View(vm);
            }
            regulation = buffered;
        }

        try
        {
            await _funds.CreateAsync(new CreateFundCommand(vm.Name, vm.Description, regulation), ActorId, ct);
        }
        catch (DuplicateFundNameException)
        {
            ModelState.AddModelError(nameof(vm.Name), AdminFundsResources.Error_DuplicateName);
            return View(vm);
        }
        catch (ArgumentException ex)
        {
            ModelState.AddModelError(nameof(vm.Name), ex.Message);
            return View(vm);
        }
        finally
        {
            if (regulation is not null) await regulation.Content.DisposeAsync();
        }

        TempData["SuccessMessage"] = AdminFundsResources.Flash_Created;
        return RedirectToAction(nameof(Index));
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Details(int id, CancellationToken ct)
    {
        var detail = await _funds.GetDetailAsync(id, ct);
        if (detail is null) return NotFound();
        return View(new AdminFundDetailsViewModel { Detail = detail });
    }

    [HttpPost("{id:int}/Edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, AdminFundEditViewModel vm, CancellationToken ct)
    {
        vm.Id = id;
        if (!ModelState.IsValid)
        {
            TempData["ErrorMessage"] = string.Join(" ",
                ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
            return RedirectToAction(nameof(Details), new { id });
        }

        try
        {
            await _funds.EditAsync(new EditFundCommand(id, vm.Name, vm.Description), ActorId, ct);
            TempData["SuccessMessage"] = AdminFundsResources.Flash_Updated;
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (DuplicateFundNameException)
        {
            TempData["ErrorMessage"] = AdminFundsResources.Error_DuplicateName;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            TempData["ErrorMessage"] = ex.Message;
        }
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost("{id:int}/Regulation")]
    [ValidateAntiForgeryToken]
    [UploadSizeGuard(FileCategory.FundRegulation)]
    public async Task<IActionResult> Regulation(int id, IFormFile? regulationFile, CancellationToken ct)
    {
        if (regulationFile is null || regulationFile.Length == 0)
        {
            TempData["ErrorMessage"] = AdminFundsResources.Error_FileRequired;
            return RedirectToAction(nameof(Details), new { id });
        }

        var buffered = await ValidatePdfAsync(regulationFile, modelKey: null, ct);
        if (buffered is null)
        {
            TempData["ErrorMessage"] = AdminFundsResources.Error_NotPdf;
            return RedirectToAction(nameof(Details), new { id });
        }

        try
        {
            await _funds.SetRegulationAsync(new SetFundRegulationCommand(id, buffered), ActorId, ct);
            TempData["SuccessMessage"] = AdminFundsResources.Flash_RegulationSet;
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        finally
        {
            await buffered.Content.DisposeAsync();
        }
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost("{id:int}/Regulation/Remove")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveRegulation(int id, CancellationToken ct)
    {
        try
        {
            await _funds.RemoveRegulationAsync(id, ActorId, ct);
            TempData["SuccessMessage"] = AdminFundsResources.Flash_RegulationRemoved;
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost("{id:int}/Archive")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Archive(int id, CancellationToken ct)
    {
        try
        {
            await _funds.ArchiveAsync(id, ActorId, ct);
            TempData["SuccessMessage"] = AdminFundsResources.Flash_Archived;
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost("{id:int}/Reactivate")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reactivate(int id, CancellationToken ct)
    {
        try
        {
            await _funds.ReactivateAsync(id, ActorId, ct);
            TempData["SuccessMessage"] = AdminFundsResources.Flash_Reactivated;
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        return RedirectToAction(nameof(Details), new { id });
    }

    /// <summary>
    /// Validates the upload is a PDF (content-type + <c>%PDF-</c> magic bytes) and
    /// buffers it into a rewound MemoryStream for the service. Returns null and
    /// (when <paramref name="modelKey"/> is set) records a ModelState error when
    /// the file is not a PDF.
    /// </summary>
    private async Task<FundRegulationUpload?> ValidatePdfAsync(IFormFile file, string? modelKey, CancellationToken ct)
    {
        var contentTypeOk = string.Equals(
            file.ContentType, "application/pdf", StringComparison.OrdinalIgnoreCase);

        var buffer = new MemoryStream();
        await file.CopyToAsync(buffer, ct);
        buffer.Position = 0;

        var head = new byte[PdfMagic.Length];
        var read = await buffer.ReadAsync(head, ct);
        buffer.Position = 0;
        var magicOk = read == PdfMagic.Length && head.AsSpan().SequenceEqual(PdfMagic);

        if (!contentTypeOk || !magicOk)
        {
            await buffer.DisposeAsync();
            if (modelKey is not null)
            {
                ModelState.AddModelError(modelKey, AdminFundsResources.Error_NotPdf);
            }
            return null;
        }

        return new FundRegulationUpload(buffer, file.FileName, "application/pdf", file.Length);
    }
}
