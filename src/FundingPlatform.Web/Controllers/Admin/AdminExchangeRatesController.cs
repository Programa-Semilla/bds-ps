using FundingPlatform.Application.Errors;
using FundingPlatform.Application.Interfaces;
using FundingPlatform.Application.Services;
using FundingPlatform.Domain.Entities;
using FundingPlatform.Domain.ValueObjects;
using FundingPlatform.Web.Filters;
using FundingPlatform.Web.Localization;
using FundingPlatform.Web.ViewModels.Admin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace FundingPlatform.Web.Controllers.Admin;

/// <summary>
/// Spec 015 / US3 / T313 — admin reference-rate history. Rates are immutable
/// once snapshotted by a Quotation (FR-008). PUT and DELETE are explicitly
/// rejected with 405 + an audit entry per
/// <c>contracts/exchange-rate-api.md</c>.
///
/// Note on role naming: codebase uses "Admin" (see other admin controllers).
/// </summary>
[Authorize(Roles = "Admin")]
[SupplierAdminDenied]
[Route("Admin/ExchangeRates")]
public class AdminExchangeRatesController : Controller
{
    private readonly IExchangeRateService _rateService;
    private readonly ICurrencyConfigService _currencyService;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IUserFacingErrorTranslator _errorTranslator;

    private static readonly string ImmutableMessage =
        "Exchange rates are immutable. Supersede by creating a new rate.";

    public AdminExchangeRatesController(
        IExchangeRateService rateService,
        ICurrencyConfigService currencyService,
        UserManager<ApplicationUser> userManager,
        IUserFacingErrorTranslator errorTranslator)
    {
        _rateService = rateService;
        _currencyService = currencyService;
        _userManager = userManager;
        _errorTranslator = errorTranslator;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index([FromQuery(Name = "json")] int? json, CancellationToken ct)
    {
        var rates = await _rateService.ListAsync(CurrencyCode.Usd, CurrencyCode.Crc, ct);
        var users = _userManager.Users.ToDictionary(u => u.Id, u => u.UserName ?? u.Email ?? u.Id);

        var rows = rates
            .Select((r, idx) => MapRow(r, users, isActive: idx == 0))
            .ToList();

        if (json == 1)
        {
            return Json(rows);
        }

        return View(new AdminExchangeRatesListViewModel { Rows = rows });
    }

    [HttpGet("Create")]
    public async Task<IActionResult> Create(CancellationToken ct)
    {
        var currencies = await _currencyService.ListEnabledAsync(ct);
        var vm = new AdminExchangeRateCreateViewModel
        {
            EffectiveAtLocal = DateTime.Now,
            AvailableCurrencies = currencies.Select(c => new AdminCurrencyRowViewModel
            {
                Code = c.Code.Value,
                Symbol = c.Symbol,
                DisplayName = c.DisplayName,
                IsEnabled = c.IsEnabled,
                IsBaseCurrency = c.IsBaseCurrency,
                DisplayOrder = c.DisplayOrder,
            }).ToList()
        };
        return View(vm);
    }

    [HttpPost("")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(AdminExchangeRateCreateViewModel vm, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            await PopulateAvailableCurrencies(vm, ct);
            return View(vm);
        }

        CurrencyCode source, target;
        try
        {
            source = CurrencyCode.From(vm.SourceCurrencyCode);
            target = CurrencyCode.From(vm.TargetCurrencyCode);
        }
        catch (ArgumentException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            await PopulateAvailableCurrencies(vm, ct);
            return View(vm);
        }

        var actorId = _userManager.GetUserId(User) ?? string.Empty;
        var effectiveUtc = vm.EffectiveAtLocal.Kind == DateTimeKind.Utc
            ? vm.EffectiveAtLocal
            : vm.EffectiveAtLocal.ToUniversalTime();

        try
        {
            await _rateService.CreateAsync(
                source, target, vm.BuyRate, vm.SellRate, effectiveUtc, actorId, ct);
        }
        catch (UserFacingException ex)
        {
            // Map domain field keys to ViewModel property names so the inline
            // <span asp-validation-for="..."> binds the error to the correct input.
            var key = ex.FieldKey switch
            {
                nameof(ExchangeRate.EffectiveAtUtc) => nameof(vm.EffectiveAtLocal),
                nameof(ExchangeRate.TargetCurrency) => nameof(vm.TargetCurrencyCode),
                _ => ex.FieldKey ?? string.Empty,
            };
            ModelState.AddModelError(key, _errorTranslator.Translate(ex.Code));
            await PopulateAvailableCurrencies(vm, ct);
            return View(vm);
        }

        TempData["SuccessMessage"] = "Tipo de cambio publicado.";
        return RedirectToAction(nameof(Index));
    }

    /// <summary>
    /// FR-008 — PUT against a rate is always rejected. Records the audit entry
    /// and returns 405. <c>{id}</c> is matched first via <c>Guid</c> route
    /// constraint and falls back to a string capture so the audit can include
    /// whatever the caller passed.
    /// </summary>
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> EditBlocked(Guid id)
    {
        var actorId = _userManager.GetUserId(User) ?? string.Empty;
        await _rateService.RecordEditAttemptAsync(id, actorId);
        Response.Headers.Append("Allow", "GET, POST");
        return StatusCode(StatusCodes.Status405MethodNotAllowed,
            new { error = ImmutableMessage });
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteBlocked(Guid id)
    {
        var actorId = _userManager.GetUserId(User) ?? string.Empty;
        await _rateService.RecordDeleteAttemptAsync(id, actorId);
        Response.Headers.Append("Allow", "GET, POST");
        return StatusCode(StatusCodes.Status405MethodNotAllowed,
            new { error = ImmutableMessage });
    }

    private async Task PopulateAvailableCurrencies(
        AdminExchangeRateCreateViewModel vm, CancellationToken ct)
    {
        var currencies = await _currencyService.ListEnabledAsync(ct);
        vm.AvailableCurrencies = currencies.Select(c => new AdminCurrencyRowViewModel
        {
            Code = c.Code.Value,
            Symbol = c.Symbol,
            DisplayName = c.DisplayName,
            IsEnabled = c.IsEnabled,
            IsBaseCurrency = c.IsBaseCurrency,
            DisplayOrder = c.DisplayOrder,
        }).ToList();
    }

    private static AdminExchangeRateRowViewModel MapRow(
        Domain.Entities.ExchangeRate r,
        Dictionary<string, string> users,
        bool isActive)
    {
        users.TryGetValue(r.CreatedByUserId, out var name);
        return new AdminExchangeRateRowViewModel
        {
            Id = r.Id,
            SourceCurrencyCode = r.SourceCurrency.Value,
            TargetCurrencyCode = r.TargetCurrency.Value,
            BuyRate = r.BuyRate,
            SellRate = r.SellRate,
            EffectiveAtUtc = r.EffectiveAtUtc,
            CreatedByUserId = r.CreatedByUserId,
            CreatedByUserName = name,
            CreatedAtUtc = r.CreatedAtUtc,
            IsUsed = r.IsUsed,
            IsActive = isActive,
        };
    }
}
