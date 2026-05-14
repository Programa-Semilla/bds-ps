using System.Security.Claims;
using FundingPlatform.Application.Abstractions;
using FundingPlatform.Application.Applications;
using FundingPlatform.Application.Applications.Commands;
using FundingPlatform.Application.Applications.Queries;
using FundingPlatform.Application.Services;
using FundingPlatform.Application.Suppliers;
using FundingPlatform.Domain.Exceptions;
using FundingPlatform.Domain.Interfaces;
using FundingPlatform.Infrastructure.Persistence;
using FundingPlatform.Web.Localization;
using FundingPlatform.Web.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FundingPlatform.Web.Controllers;

[Authorize(Roles = "Applicant")]
public class ApplicationController : Controller
{
    private readonly ApplicationService _applicationService;
    private readonly AppDbContext _dbContext;
    private readonly IUserFacingErrorTranslator _errorTranslator;
    private readonly IAutosaveFieldHandler _autosaveHandler;
    private readonly ISubmitApplicationHandler _submitHandler;
    private readonly IGetApplicationReviewProjection _reviewProjection;
    private readonly ISearchSuppliersHandler _supplierSearchHandler;
    private readonly ICreateSupplierBranchHandler _createBranchHandler;
    private readonly IStageExpiryEvaluator _stageExpiry;
    private readonly IStageExpiryClock _stageExpiryClock;

    public ApplicationController(
        ApplicationService applicationService,
        AppDbContext dbContext,
        IUserFacingErrorTranslator errorTranslator,
        IAutosaveFieldHandler autosaveHandler,
        ISubmitApplicationHandler submitHandler,
        IGetApplicationReviewProjection reviewProjection,
        ISearchSuppliersHandler supplierSearchHandler,
        ICreateSupplierBranchHandler createBranchHandler,
        IStageExpiryEvaluator stageExpiry,
        IStageExpiryClock stageExpiryClock)
    {
        _applicationService = applicationService;
        _dbContext = dbContext;
        _errorTranslator = errorTranslator;
        _autosaveHandler = autosaveHandler;
        _submitHandler = submitHandler;
        _reviewProjection = reviewProjection;
        _supplierSearchHandler = supplierSearchHandler;
        _createBranchHandler = createBranchHandler;
        _stageExpiry = stageExpiry;
        _stageExpiryClock = stageExpiryClock;
    }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var applicantId = await GetCurrentApplicantIdAsync();
        var applications = await _applicationService.GetApplicationsForApplicantAsync(applicantId);

        // Spec 021 / FR-030 — "Hola, {Nombre}" greeting; pulled from the
        // applicant's first name (free text on the Applicant aggregate).
        var greetingName = await _dbContext.Applicants
            .Where(a => a.Id == applicantId)
            .Select(a => a.FirstName)
            .FirstOrDefaultAsync();

        var viewModel = new ApplicationListViewModel
        {
            GreetingName = greetingName,
            Applications = applications.Select(a => new ApplicationListItemViewModel
            {
                Id = a.Id,
                PublicCode = a.PublicCode,
                CompanyName = a.CompanyName,
                State = a.State.ToString(),
                ItemCount = a.ItemCount,
                CreatedAt = a.CreatedAt,
                UpdatedAt = a.UpdatedAt,
                SubmittedAt = a.SubmittedAt
            }).ToList()
        };

        return View(viewModel);
    }

    [HttpGet]
    public IActionResult Create()
    {
        return View(new CreateApplicationViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CreateApplicationViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var applicantId = await GetCurrentApplicantIdAsync();
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var command = new CreateApplicationCommand(applicantId, model.CompanyName);
        var result = await _applicationService.CreateApplicationAsync(command, userId);

        if (result.Error is not null)
        {
            ModelState.AddModelError(nameof(CreateApplicationViewModel.CompanyName),
                _errorTranslator.Translate(result.Error));
            return View(model);
        }

        TempData["SuccessMessage"] = "Solicitud creada con éxito.";
        return RedirectToAction(nameof(Details), new { id = result.ApplicationId });
    }

    [HttpGet]
    public async Task<IActionResult> Details(int id)
    {
        var applicantId = await GetCurrentApplicantIdAsync();
        var application = await _applicationService.GetApplicationAsync(id);

        if (application is null || application.ApplicantId != applicantId)
        {
            return NotFound();
        }

        var viewModel = MapToViewModel(application);
        return View(viewModel);
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        var applicantId = await GetCurrentApplicantIdAsync();
        var application = await _applicationService.GetApplicationAsync(id);

        if (application is null || application.ApplicantId != applicantId)
        {
            return NotFound();
        }

        // Spec 021 / T119 / FR-024 — populate ViewData with the stage countdown
        // banner ViewModel so the partial in Edit.cshtml can render the live
        // window (or the "Vencido" red state when closed).
        await PopulateStageBannerAsync(id);

        var viewModel = MapToViewModel(application);
        return View(viewModel);
    }

    /// <summary>
    /// Spec 021 / T094 / FR-017 — read-only <c>/review</c> page that renders
    /// the summary before final submit. PublicCode-bound URL per the contract
    /// in <c>applicant-routes.md</c>; the legacy numeric route is preserved
    /// via the <c>/Applications/{publicCode}/Review</c> alias below.
    /// </summary>
    [HttpGet]
    [Route("Applications/{publicCode}/Review")]
    public async Task<IActionResult> Review(string publicCode)
    {
        var applicantId = await GetCurrentApplicantIdAsync();
        var review = await _reviewProjection.ExecuteAsync(publicCode, applicantId);
        if (review is null)
        {
            return NotFound();
        }
        // Spec 021 / T119 / FR-024 — surface the stage countdown banner on the
        // /review surface so the applicant sees the live remaining-time before
        // pressing "Confirmar y enviar".
        await PopulateStageBannerAsync(review.Id);
        return View(review);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Route("Application/{id}/Submit")]
    public async Task<IActionResult> Submit(int id)
    {
        var applicantId = await GetCurrentApplicantIdAsync();
        var application = await _applicationService.GetApplicationAsync(id);

        if (application is null || application.ApplicantId != applicantId)
        {
            return NotFound();
        }

        try
        {
            // Spec 021 / T091 — route through the stage-aware submit handler so
            // FR-006 / FR-017 guards fire (and StageWindowClosedException is
            // mapped to 422 by the global DomainExceptionFilter).
            await _submitHandler.SubmitAsync(new SubmitApplicationCommand(id));
            TempData["SuccessMessage"] = "Solicitud enviada con éxito.";
            return RedirectToAction(nameof(Details), new { id });
        }
        catch (InvalidOperationException ex)
        {
            var message = ex.Message;
            const string prefix = "Cannot submit application: ";
            var errors = message.StartsWith(prefix)
                ? message[prefix.Length..].Split("; ").ToList()
                : new List<string> { message };
            TempData["ValidationErrors"] = System.Text.Json.JsonSerializer.Serialize(errors);
            return RedirectToAction(nameof(Details), new { id });
        }
    }

    /// <summary>
    /// Spec 021 / T094 / FR-017 — PublicCode-bound submit alias used by the
    /// <c>/review</c> "Confirmar y enviar" form. Resolves the Application via
    /// PublicCode and dispatches to <see cref="Submit(int)"/>.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Route("Applications/{publicCode}/Submit")]
    public async Task<IActionResult> SubmitByPublicCode(string publicCode)
    {
        var applicantId = await GetCurrentApplicantIdAsync();
        var canonical = publicCode.Trim().ToUpperInvariant();
        var application = await _dbContext.Applications
            .FirstOrDefaultAsync(a => EF.Property<string>(a, "PublicCode") == canonical);
        if (application is null || application.ApplicantId != applicantId)
        {
            return NotFound();
        }
        return await Submit(application.Id);
    }

    /// <summary>
    /// Spec 021 / T094 / R-5 / FR-016 — per-field autosave endpoint.
    /// Wraps <see cref="AutosaveFieldHandler"/>. Returns 200 with the new
    /// ETag + savedAt on success, 409 on ETag mismatch, 422 on stage-window
    /// closed (via the global filter), 400 on unknown fieldKey.
    /// </summary>
    [HttpPost]
    [Route("api/applications/{publicCode}/autosave")]
    public async Task<IActionResult> Autosave(string publicCode, [FromBody] AutosaveRequest body)
    {
        if (body is null)
        {
            return BadRequest();
        }
        var applicantId = await GetCurrentApplicantIdAsync();
        try
        {
            var result = await _autosaveHandler.HandleAsync(
                new AutosaveFieldCommand(publicCode, body.FieldKey ?? string.Empty, body.Value, body.Etag),
                applicantId);
            return Ok(new { etag = result.Etag, savedAt = result.SavedAt });
        }
        catch (AutosaveConflictException)
        {
            return Conflict(new ProblemDetails
            {
                Title = "ETag desactualizado",
                Detail = "La solicitud cambió desde que abrió el editor. Recargue la página.",
                Status = StatusCodes.Status409Conflict,
            });
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Campo no soportado",
                Detail = ex.Message,
                Status = StatusCodes.Status400BadRequest,
            });
        }
    }

    /// <summary>
    /// Spec 021 / T094 / FR-009 — applicant-side supplier autocomplete.
    /// </summary>
    [HttpGet]
    [Route("api/applications/suppliers/search")]
    public async Task<IActionResult> SearchSuppliers([FromQuery] string q)
    {
        var applicantId = await GetCurrentApplicantIdAsync();
        var results = await _supplierSearchHandler.HandleAsync(
            new SearchSuppliersQuery(q ?? string.Empty, applicantId));
        return Ok(results.Select(r => new
        {
            id = r.Id,
            name = r.Name,
            cedulaJuridica = r.CedulaJuridica,
        }));
    }

    /// <summary>
    /// Spec 021 / T094 / FR-012 / FR-014 — applicant-side inline branch
    /// registration (supplier search no-match path).
    /// </summary>
    [HttpPost]
    [Route("api/applications/suppliers/create-branch")]
    public async Task<IActionResult> CreateSupplierBranch([FromBody] CreateBranchRequest body)
    {
        if (body is null)
        {
            return BadRequest();
        }
        var applicantId = await GetCurrentApplicantIdAsync();
        try
        {
            var result = await _createBranchHandler.HandleAsync(new CreateSupplierBranchCommand(
                SupplierId: body.SupplierId,
                LegalId: body.LegalId,
                SupplierName: body.SupplierName,
                BranchName: body.BranchName ?? "Sucursal principal",
                ContactPersonName: body.ContactPersonName,
                Email: body.Email,
                Phone: body.Phone,
                AddressLine: body.AddressLine,
                ProvinceId: body.ProvinceId,
                CantonId: body.CantonId,
                CurrentApplicantId: applicantId));
            return Ok(new { supplierId = result.SupplierId, branchId = result.BranchId });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Datos inválidos",
                Detail = ex.Message,
                Status = StatusCodes.Status400BadRequest,
            });
        }
    }

    private async Task<int> GetCurrentApplicantIdAsync()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var applicant = await _dbContext.Applicants
            .FirstOrDefaultAsync(a => a.UserId == userId);

        return applicant?.Id ?? throw new InvalidOperationException("Applicant not found for current user.");
    }

    /// <summary>
    /// Spec 021 / T119 / FR-024 — composes the
    /// <see cref="StageCountdownBannerViewModel"/> for the supplied Application
    /// and stashes it in <c>ViewData["StageBanner"]</c> so Edit / Review views
    /// can render the <c>_StageCountdownBanner</c> partial.
    ///
    /// Terminal-state Applications (AgreementExecuted) have no live window and
    /// receive a null banner (the partial is skipped on the view side).
    /// </summary>
    private async Task PopulateStageBannerAsync(int applicationId)
    {
        var entity = await _dbContext.Applications
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == applicationId);
        if (entity is null)
        {
            ViewData["StageBanner"] = null;
            return;
        }

        var (stage, enteredAt, closesAt) = await _stageExpiry.EvaluateForAsync(entity);
        var now = _stageExpiryClock.UtcNow;
        ViewData["StageBanner"] = new StageCountdownBannerViewModel
        {
            StageKind = stage,
            EnteredAt = enteredAt,
            ClosesAt = closesAt,
            Now = now,
            Closed = now >= closesAt,
        };
    }

    private static ApplicationViewModel MapToViewModel(FundingPlatform.Application.DTOs.ApplicationDto dto)
    {
        var vm = new ApplicationViewModel
        {
            Id = dto.Id,
            PublicCode = dto.PublicCode,
            CompanyName = dto.CompanyName,
            State = dto.State.ToString(),
            CreatedAt = dto.CreatedAt,
            UpdatedAt = dto.UpdatedAt,
            SubmittedAt = dto.SubmittedAt,
            Items = dto.Items.Select(i => new ItemViewModel
            {
                Id = i.Id,
                ProductName = i.ProductName,
                CategoryName = i.CategoryName,
                QuotationCount = i.Quotations.Count,
                HasImpact = i.Impact is not null,
                ReviewComment = i.ReviewComment,
                SelectedSupplierId = i.SelectedSupplierId,
                Quotations = i.Quotations.Select(q => new QuotationSummaryViewModel
                {
                    Id = q.Id,
                    SupplierName = q.SupplierName,
                    Price = q.Price,
                    Currency = q.Currency,
                    ConvertedCrcAmount = q.ConvertedCrcAmount,
                    SnapshotRateValue = q.SnapshotRateValue,
                    SnapshotRateType = q.SnapshotRateType,
                    SnapshotEffectiveAtUtc = q.SnapshotEffectiveAtUtc,
                    LegacyNeedsReview = q.LegacyNeedsReview
                }).ToList()
            }).ToList()
        };

        decimal? total = null;
        var hasLegacy = false;
        foreach (var item in vm.Items)
        {
            if (item.SelectedSupplierId is null) continue;
            var chosen = item.Quotations
                .FirstOrDefault(q => GetSupplierIdForQuotation(dto, item.Id, q.Id) == item.SelectedSupplierId);
            if (chosen is null) continue;
            if (chosen.LegacyNeedsReview)
            {
                hasLegacy = true;
                continue;
            }
            if (chosen.ConvertedCrcAmount.HasValue)
            {
                total = (total ?? 0m) + chosen.ConvertedCrcAmount.Value;
            }
        }
        vm.TotalConvertedCrc = total;
        vm.HasLegacyNeedsReview = hasLegacy;
        return vm;
    }

    private static int? GetSupplierIdForQuotation(
        FundingPlatform.Application.DTOs.ApplicationDto dto,
        int itemId,
        int quotationId)
    {
        var item = dto.Items.FirstOrDefault(i => i.Id == itemId);
        var q = item?.Quotations.FirstOrDefault(qq => qq.Id == quotationId);
        return q?.SupplierId;
    }

    /// <summary>Autosave POST body. Spec 021 / R-5.</summary>
    public sealed class AutosaveRequest
    {
        public string? FieldKey { get; set; }
        public string? Value { get; set; }
        public string? Etag { get; set; }
    }

    /// <summary>Create-branch POST body. Spec 021 / FR-012 / FR-014.</summary>
    public sealed class CreateBranchRequest
    {
        public int? SupplierId { get; set; }
        public string? LegalId { get; set; }
        public string? SupplierName { get; set; }
        public string? BranchName { get; set; }
        public string? ContactPersonName { get; set; }
        public string? Email { get; set; }
        public string? Phone { get; set; }
        public string? AddressLine { get; set; }
        public int ProvinceId { get; set; }
        public int CantonId { get; set; }
    }
}
