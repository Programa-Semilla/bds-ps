using FundingPlatform.Application.Interfaces;
using FundingPlatform.Domain.Entities;
using FundingPlatform.Domain.ValueObjects;
using FundingPlatform.Web.ViewModels.Admin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace FundingPlatform.Web.Controllers.Admin;

/// <summary>
/// Spec 015 / US3 / T312 — admin currency catalog. Two rows in MVP (CRC + USD).
/// CRC is the platform's permanent base currency: enable/disable on CRC is a
/// 409 (FR-002). The Razor view also exposes <c>?json=1</c> for tests per the
/// contract.
///
/// Note on role naming: the spec contract uses "Administrator", but this
/// codebase wires the Identity role as "Admin" (see other admin controllers
/// — <c>AdminUsersController</c>, <c>AdminSuppliersController</c>). Using
/// "Admin" here keeps role-claim handling consistent with the rest of the app.
/// </summary>
[Authorize(Roles = "Admin")]
[Route("Admin/Currencies")]
public class AdminCurrenciesController : Controller
{
    private readonly ICurrencyConfigService _currencyService;
    private readonly UserManager<ApplicationUser> _userManager;

    public AdminCurrenciesController(
        ICurrencyConfigService currencyService,
        UserManager<ApplicationUser> userManager)
    {
        _currencyService = currencyService;
        _userManager = userManager;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index([FromQuery(Name = "json")] int? json, CancellationToken ct)
    {
        var rows = await _currencyService.ListAllAsync(ct);
        var view = rows.Select(MapRow).ToList();

        if (json == 1)
        {
            return Json(view);
        }

        return View(new AdminCurrenciesListViewModel { Rows = view });
    }

    [HttpPost("{code}/Enable")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Enable(string code, CancellationToken ct)
    {
        if (!TryParseCode(code, out var parsed))
        {
            return NotFound();
        }

        var rows = await _currencyService.ListAllAsync(ct);
        if (!rows.Any(c => c.Code == parsed))
        {
            return NotFound();
        }

        var actorId = _userManager.GetUserId(User) ?? string.Empty;
        await _currencyService.EnableAsync(parsed, actorId, ct);

        TempData["SuccessMessage"] = $"Moneda '{parsed}' habilitada.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("{code}/Disable")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Disable(string code, CancellationToken ct)
    {
        if (!TryParseCode(code, out var parsed))
        {
            return NotFound();
        }

        var rows = await _currencyService.ListAllAsync(ct);
        var existing = rows.FirstOrDefault(c => c.Code == parsed);
        if (existing is null)
        {
            return NotFound();
        }

        var actorId = _userManager.GetUserId(User) ?? string.Empty;
        try
        {
            await _currencyService.DisableAsync(parsed, actorId, ct);
        }
        catch (InvalidOperationException)
        {
            // FR-002 — CRC is the system base currency and cannot be disabled.
            TempData["ErrorMessage"] =
                "CRC es la moneda base del sistema y no se puede deshabilitar.";
            return RedirectToAction(nameof(Index));
        }

        TempData["SuccessMessage"] = $"Moneda '{parsed}' deshabilitada.";
        return RedirectToAction(nameof(Index));
    }

    private static bool TryParseCode(string code, out CurrencyCode parsed)
    {
        try
        {
            parsed = CurrencyCode.From(code);
            return true;
        }
        catch (ArgumentException)
        {
            parsed = null!;
            return false;
        }
    }

    private static AdminCurrencyRowViewModel MapRow(Currency c) => new()
    {
        Code = c.Code.Value,
        Symbol = c.Symbol,
        DisplayName = c.DisplayName,
        IsEnabled = c.IsEnabled,
        IsBaseCurrency = c.IsBaseCurrency,
        DisplayOrder = c.DisplayOrder,
    };
}
