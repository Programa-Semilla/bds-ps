using FundingPlatform.Application.Interfaces;
using FundingPlatform.Domain.Entities;
using FundingPlatform.Domain.ValueObjects;
using FundingPlatform.Web.Filters;
using FundingPlatform.Web.ViewModels.Admin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace FundingPlatform.Web.Controllers.Admin;

/// <summary>
/// Spec 015 / US6 / T611 — admin queue for legacy quotations that need a
/// historical rate attached. Two surfaces (route normalized in spec 017 US5):
///   - <c>GET /Admin/LegacyQuotations</c>: list the flagged queue with a
///     per-row rate picker populated from the rate-history.
///   - <c>POST /Admin/LegacyQuotations/Attach</c>: bind {quotationId,
///     rateId}, call the application service, and redirect back to the list.
///
/// Note on role naming: codebase uses "Admin" (see other admin controllers).
/// </summary>
[Authorize(Roles = "Admin,Auditor")]
[SupplierAdminDenied]
[Route("Admin/LegacyQuotations")]
public class AdminLegacyQuotationsController : Controller
{
    private readonly ILegacyQuotationRateAttachService _service;
    private readonly IExchangeRateService _rateService;
    private readonly UserManager<ApplicationUser> _userManager;

    public AdminLegacyQuotationsController(
        ILegacyQuotationRateAttachService service,
        IExchangeRateService rateService,
        UserManager<ApplicationUser> userManager)
    {
        _service = service;
        _rateService = rateService;
        _userManager = userManager;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var rows = await _service.ListAsync(ct);

        // MVP only ships USD↔CRC, so a single rate-history fetch covers every
        // flagged row. If additional currency pairs land later this becomes a
        // per-row dictionary lookup keyed on the row's currency.
        var rates = await _rateService.ListAsync(CurrencyCode.Usd, CurrencyCode.Crc, ct);
        var rateOptions = rates
            .Select(r => new AdminLegacyQuotationRateOption
            {
                Id = r.Id,
                BuyRate = r.BuyRate,
                EffectiveAtUtc = r.EffectiveAtUtc,
            })
            .ToList();

        var vm = new AdminLegacyQuotationsListViewModel
        {
            Rows = rows.Select(r => new AdminLegacyQuotationRowViewModel
            {
                QuotationId = r.QuotationId,
                ApplicationId = r.ApplicationId,
                ItemId = r.ItemId,
                ItemName = r.ItemName,
                SupplierName = r.SupplierName,
                Price = r.Price,
                Currency = r.Currency,
                CreatedAt = r.CreatedAt,
                RateOptions = rateOptions,
            }).ToList(),
        };

        return View(vm);
    }

    [HttpPost("Attach")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Attach(int quotationId, Guid rateId, CancellationToken ct)
    {
        if (quotationId <= 0)
        {
            TempData["ErrorMessage"] = "Cotización inválida.";
            return RedirectToAction(nameof(Index));
        }
        if (rateId == Guid.Empty)
        {
            TempData["ErrorMessage"] = "Seleccione un tipo de cambio del histórico.";
            return RedirectToAction(nameof(Index));
        }

        var actorId = _userManager.GetUserId(User) ?? string.Empty;
        try
        {
            await _service.AttachAsync(quotationId, rateId, actorId, ct);
        }
        catch (InvalidOperationException ex)
        {
            TempData["ErrorMessage"] = ex.Message;
            return RedirectToAction(nameof(Index));
        }

        TempData["SuccessMessage"] = $"Tipo de cambio asignado a la cotización #{quotationId}.";
        return RedirectToAction(nameof(Index));
    }
}
