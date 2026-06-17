using System.Security.Claims;
using FundingPlatform.Application.Abstractions;
using FundingPlatform.Application.Applications;
using FundingPlatform.Application.Applications.Commands;
using FundingPlatform.Application.Applications.Queries;
using FundingPlatform.Application.Services;
using FundingPlatform.Application.Suppliers;
using FundingPlatform.Domain.Entities;
using FundingPlatform.Domain.Exceptions;
using FundingPlatform.Domain.Interfaces;
using FundingPlatform.Infrastructure.Persistence;
using FundingPlatform.Web.Localization;
using FundingPlatform.Web.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
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
    private readonly ICategoryRepository _categoryRepository;

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
        IStageExpiryClock stageExpiryClock,
        ICategoryRepository categoryRepository)
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
        _categoryRepository = categoryRepository;
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
    public async Task<IActionResult> Create()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var applicantId = await GetCurrentApplicantIdAsync();
        var model = new CreateApplicationViewModel();
        await PopulateEligibleGroupsAsync(model, userId);
        // Spec 037 / FR-002 — populate the company selector (0/1/many rules).
        PopulateActiveCompaniesFrom(model, await ResolveActiveCompaniesAsync(applicantId));
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CreateApplicationViewModel model)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var applicantId = await GetCurrentApplicantIdAsync();

        // Spec 029 / FR-018 — resolve the applicant's eligible Groups and validate
        // the chosen anchor against that set (defense against tampering + the
        // 0/1/many rendering rules). Re-populate for redisplay on any failure.
        var eligible = await ResolveEligibleGroupsAsync(userId);
        // Spec 037 / FR-018/019 — resolve the applicant's active companies for the
        // same defense + 0/1/many rendering.
        var companies = await ResolveActiveCompaniesAsync(applicantId);

        if (eligible.Count == 0 || companies.Count == 0)
        {
            PopulateEligibleGroupsFrom(model, eligible);
            PopulateActiveCompaniesFrom(model, companies);
            return View(model);
        }
        if (model.GroupId is null || eligible.All(g => g.GroupId != model.GroupId.Value))
        {
            ModelState.AddModelError(nameof(CreateApplicationViewModel.GroupId),
                "Debe seleccionar un proceso activo válido para postular.");
        }
        // Spec 037 / FR-018 — the posted CompanyId must be one of the applicant's
        // active companies (tamper defense; no disclosure).
        if (model.CompanyId is null || companies.All(c => c.Id != model.CompanyId.Value))
        {
            ModelState.AddModelError(nameof(CreateApplicationViewModel.CompanyId),
                "Debe seleccionar una empresa válida.");
        }

        if (!ModelState.IsValid)
        {
            PopulateEligibleGroupsFrom(model, eligible);
            PopulateActiveCompaniesFrom(model, companies);
            return View(model);
        }

        var command = new CreateApplicationCommand(applicantId, model.CompanyId!.Value, model.GroupId!.Value);
        var result = await _applicationService.CreateApplicationAsync(command, userId);

        if (result.Error is not null)
        {
            ModelState.AddModelError(nameof(CreateApplicationViewModel.CompanyId),
                _errorTranslator.Translate(result.Error));
            PopulateEligibleGroupsFrom(model, eligible);
            PopulateActiveCompaniesFrom(model, companies);
            return View(model);
        }

        // Spec 021 / US2 — a fresh draft opens straight in the draft editor;
        // Details is a read-only summary and is never an editing surface.
        TempData["SuccessMessage"] = "Borrador creado. Complete el impacto y los ítems para enviarlo.";
        return RedirectToAction(nameof(Edit), new { id = result.ApplicationId });
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

        await PopulateRegulationLinkAsync(id);
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

        // Spec 021 / US2 — the draft editor only edits Drafts. A submitted (or
        // later-state) Application is read-only; route it to the summary.
        if (application.State != FundingPlatform.Domain.Enums.ApplicationState.Draft)
        {
            return RedirectToAction(nameof(Details), new { id });
        }

        // Spec 021 / T119 / FR-024 — populate ViewData with the stage countdown
        // banner ViewModel so the partial in Edit.cshtml can render the live
        // window (or the "Vencido" red state when closed).
        await PopulateStageBannerAsync(id);
        await PopulateRegulationLinkAsync(id);

        var viewModel = MapToViewModel(application);
        var categories = await _categoryRepository.GetAllActiveAsync();
        viewModel.Categories = categories
            .Select(c => new SelectListItem { Value = c.Id.ToString(), Text = c.Name })
            .ToList();

        // Spec 037 / FR-015 — draft re-select dropdown: the applicant's active
        // companies + the application's current company reference (the snapshot is
        // already on the view model as CompanyName).
        var companies = await ResolveActiveCompaniesAsync(applicantId);
        viewModel.CompanyId = await _dbContext.Applications
            .Where(a => a.Id == id)
            .Select(a => a.CompanyId)
            .FirstOrDefaultAsync();
        viewModel.Companies = companies
            .Select(c => new SelectListItem(
                c.Name,
                c.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)))
            .ToList();
        return View(viewModel);
    }

    /// <summary>
    /// Spec 035 / US2 — impact parameter descriptors for an ImpactTemplate,
    /// fetched by the per-item form when the applicant picks a template. The route
    /// is kept from spec 021 (now consumed by the item form, not an app-level step).
    /// </summary>
    [HttpGet]
    [Route("Application/{id}/Impact/TemplateParameters/{templateId}")]
    public async Task<IActionResult> ImpactTemplateParameters(int id, int templateId)
    {
        var templates = await _applicationService.GetImpactTemplatesAsync();
        var template = templates.FirstOrDefault(t => t.Id == templateId);
        if (template is null)
        {
            return NotFound();
        }
        return Json(template.Parameters.Select(p => new
        {
            id = p.Id,
            name = p.Name,
            displayLabel = p.DisplayLabel,
            dataType = (int)Enum.Parse<FundingPlatform.Domain.Enums.ParameterDataType>(p.DataType),
            isRequired = p.IsRequired,
        }));
    }

    // ---- Spec 035 (evolved 2026-06-16, US2 / D15) — application-level impacts manager ----

    [HttpGet]
    [Route("Application/{id}/Impacts")]
    public async Task<IActionResult> Impacts(int id)
    {
        await VerifyOwnershipAsync(id);
        var vm = await BuildImpactsViewModelAsync(id);
        return View(vm);
    }

    [HttpPost]
    [Route("Application/{id}/Impacts/Add")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddImpact(int id, int impactTemplateId, Dictionary<int, string?>? parameterValues)
    {
        await VerifyOwnershipAsync(id);

        try
        {
            await _applicationService.AddApplicationImpactAsync(
                new AddApplicationImpactCommand(id, impactTemplateId, parameterValues ?? new()));
            TempData["SuccessMessage"] = "Impacto agregado con éxito.";
        }
        catch (InvalidOperationException ex)
        {
            TempData["ErrorMessage"] = ex.Message;
        }

        return RedirectToAction(nameof(Impacts), new { id });
    }

    [HttpPost]
    [Route("Application/{id}/Impacts/{applicationImpactId}/Remove")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveImpact(int id, int applicationImpactId)
    {
        await VerifyOwnershipAsync(id);

        await _applicationService.RemoveApplicationImpactAsync(
            new RemoveApplicationImpactCommand(id, applicationImpactId));
        TempData["SuccessMessage"] = "Impacto eliminado.";

        return RedirectToAction(nameof(Impacts), new { id });
    }

    private async Task<ApplicationImpactsViewModel> BuildImpactsViewModelAsync(int id)
    {
        var dto = await _applicationService.GetApplicationAsync(id);
        var templates = await _applicationService.GetImpactTemplatesAsync();

        return new ApplicationImpactsViewModel
        {
            ApplicationId = id,
            CompanyName = dto?.CompanyName,
            DeclaredImpacts = (dto?.Impacts ?? new()).Select(ai => new DeclaredImpactRow
            {
                ApplicationImpactId = ai.Id,
                TemplateName = ai.ImpactTemplateName,
                Parameters = ai.ParameterValues.Select(pv => new ImpactParameterDisplayViewModel
                {
                    Name = pv.ParameterName,
                    DisplayLabel = pv.ParameterDisplayLabel,
                    Value = pv.Value ?? string.Empty,
                }).ToList(),
            }).ToList(),
            ActiveTemplates = templates
                .Select(t => new SelectListItem { Value = t.Id.ToString(), Text = t.Name })
                .ToList(),
        };
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

        if (await IsApplicationFrozenAsync(id))
        {
            TempData["ErrorMessage"] = FrozenToast;
            return RedirectToAction(nameof(Details), new { id });
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
    /// Spec 021 / US9 / FR-035–FR-041 — applicant-initiated delete (Draft) or
    /// withdrawal (Submitted / UnderReview). State + ownership are enforced in
    /// <see cref="ApplicationService.RemoveByApplicantAsync"/>; this action maps the
    /// outcome to a redirect + flash message. Terminal states and cross-user
    /// requests are rejected without mutating (FR-037 / FR-041).
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Remove(int id)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
        {
            return Forbid();
        }

        if (await IsApplicationFrozenAsync(id))
        {
            TempData["ErrorMessage"] = FrozenToast;
            return RedirectToAction(nameof(Index));
        }

        var result = await _applicationService.RemoveByApplicantAsync(
            id, userId, HttpContext.RequestAborted);

        if (result.NotFound)
        {
            return NotFound();
        }

        if (result.RejectedState)
        {
            TempData["ErrorMessage"] = "La solicitud ya no puede retirarse.";
            return RedirectToAction(nameof(Index));
        }

        TempData["SuccessMessage"] = result.Kind == ApplicantRemovalKind.DraftDeleted
            ? "Borrador eliminado."
            : "Solicitud retirada.";
        return RedirectToAction(nameof(Index));
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
        // PublicCode is a value-object column (HasConversion); compare the VO
        // directly so EF applies the converter — never EF.Property<string>.
        FundingPlatform.Domain.ValueObjects.PublicCode codeVo;
        try { codeVo = new FundingPlatform.Domain.ValueObjects.PublicCode(publicCode); }
        catch (ArgumentException) { return NotFound(); }
        var application = await _dbContext.Applications
            .FirstOrDefaultAsync(a => a.PublicCode == codeVo);
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

        // Spec 029 / FR-021 — reject autosaves to an archived-Fund application.
        FundingPlatform.Domain.ValueObjects.PublicCode? canonical = null;
        try { canonical = new FundingPlatform.Domain.ValueObjects.PublicCode(publicCode); }
        catch { /* malformed code → let the handler resolve/404 below */ }
        if (canonical is not null && await _dbContext.Applications.AnyAsync(a =>
                a.PublicCode == canonical
                && a.Group!.Process!.Fund!.Status == FundingPlatform.Domain.Enums.FundStatus.Archived))
        {
            return UnprocessableEntity(new ProblemDetails
            {
                Title = "Fondo archivado",
                Detail = FrozenToast,
                Status = StatusCodes.Status422UnprocessableEntity,
            });
        }

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
        catch (InvalidOperationException)
        {
            // Spec 037 / FR-015/FR-019 — e.g. a re-select against a non-Draft application,
            // or an unresolvable PublicCode. Reject without a 500 or information leak.
            return BadRequest(new ProblemDetails
            {
                Title = "Operación no permitida",
                Detail = "No se pudo guardar el cambio.",
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
    /// Spec 035 (evolved 2026-06-16) — asserts the current applicant owns the application
    /// before mutating its impacts (mirrors the ItemController guard).
    /// </summary>
    private async Task VerifyOwnershipAsync(int appId)
    {
        var applicantId = await GetCurrentApplicantIdAsync();
        var application = await _applicationService.GetApplicationAsync(appId);
        if (application is null || application.ApplicantId != applicantId)
        {
            throw new UnauthorizedAccessException("You do not own this application.");
        }
    }

    /// <summary>
    /// Spec 029 / FR-013 — when the application's anchored Fund is Active and
    /// carries a regulation, stash the download target in ViewData so the
    /// applicant Edit/Details surfaces can render the link (and nothing otherwise).
    /// </summary>
    private async Task PopulateRegulationLinkAsync(int applicationId)
    {
        var reg = await _dbContext.Applications
            .Where(a => a.Id == applicationId)
            .Select(a => new
            {
                FundId = a.Group!.Process!.Fund!.Id,
                a.Group!.Process!.Fund!.Status,
                HasRegulation = a.Group!.Process!.Fund!.RegulationBlobKey != null,
            })
            .FirstOrDefaultAsync();

        if (reg is not null
            && reg.Status == FundingPlatform.Domain.Enums.FundStatus.Active
            && reg.HasRegulation)
        {
            ViewData["RegulationFundId"] = reg.FundId;
        }
        else
        {
            ViewData["RegulationFundId"] = null;
        }
    }

    /// <summary>Spec 029 / FR-021 — es-CR message when a frozen application is mutated.</summary>
    private const string FrozenToast =
        "El fondo que rige esta postulación está archivado. No se permiten cambios.";

    /// <summary>
    /// Spec 029 / FR-021 — controller-boundary freeze guard: true when the
    /// application's anchored Fund is Archived. Primary enforcement for mutation
    /// (the domain guard is defense-in-depth). Applicants are never admins here.
    /// </summary>
    private Task<bool> IsApplicationFrozenAsync(int applicationId)
        => _dbContext.Applications.AnyAsync(a => a.Id == applicationId
            && a.Group!.Process!.Fund!.Status == FundingPlatform.Domain.Enums.FundStatus.Archived);

    /// <summary>Spec 029 / FR-018 — one eligible Group for the create-flow anchor.</summary>
    private sealed record EligibleGroup(int GroupId, string ProcessName, string GroupName);

    /// <summary>
    /// Spec 029 / FR-018 — the Groups the applicant's user is a member of whose
    /// Process is Active AND whose Fund is Active. These are the only valid
    /// anchors for a new application.
    /// </summary>
    private async Task<IReadOnlyList<EligibleGroup>> ResolveEligibleGroupsAsync(string userId)
    {
        return await _dbContext.UserGroupMemberships
            .Where(m => m.UserId == userId
                && m.Group!.Process!.Status == Domain.Enums.ProcessStatus.Active
                && m.Group!.Process!.Fund!.Status == Domain.Enums.FundStatus.Active)
            .OrderBy(m => m.Group!.Process!.Name)
            .ThenBy(m => m.Group!.Name)
            .Select(m => new EligibleGroup(m.GroupId, m.Group!.Process!.Name, m.Group!.Name))
            .ToListAsync();
    }

    /// <summary>Resolves the applicant's eligible groups then fills the view model.</summary>
    private async Task PopulateEligibleGroupsAsync(CreateApplicationViewModel model, string userId)
        => PopulateEligibleGroupsFrom(model, await ResolveEligibleGroupsAsync(userId));

    /// <summary>Spec 037 / FR-002 — one active company for the create-flow selector.</summary>
    private sealed record ActiveCompany(int Id, string Name);

    /// <summary>
    /// Spec 037 / FR-002 / FR-018 — the applicant's active (non-archived) companies,
    /// ordered by name. These are the only valid selections for a new application.
    /// </summary>
    private async Task<IReadOnlyList<ActiveCompany>> ResolveActiveCompaniesAsync(int applicantId)
    {
        return await _dbContext.Companies
            .Where(c => c.ApplicantId == applicantId && c.ArchivedAt == null)
            .OrderBy(c => c.Name)
            .Select(c => new ActiveCompany(c.Id, c.Name))
            .ToListAsync();
    }

    /// <summary>
    /// Spec 037 / FR-012–FR-014 — applies the 0/1/many rendering rules to the view
    /// model's company fields (mirrors the Group anchor).
    /// </summary>
    private static void PopulateActiveCompaniesFrom(
        CreateApplicationViewModel model, IReadOnlyList<ActiveCompany> companies)
    {
        model.HasNoCompanies = companies.Count == 0;
        model.IsSingleCompany = companies.Count == 1;

        model.Companies = companies
            .Select(c => new SelectListItem(
                c.Name,
                c.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)))
            .ToList();

        if (companies.Count == 1 && model.CompanyId is null)
        {
            model.CompanyId = companies[0].Id;
        }
    }

    /// <summary>
    /// Spec 029 / FR-018 — applies the 0/1/many rendering rules to the view model.
    /// When ambiguous (≥2 eligible) each option is disambiguated by its Group name.
    /// </summary>
    private static void PopulateEligibleGroupsFrom(
        CreateApplicationViewModel model, IReadOnlyList<EligibleGroup> eligible)
    {
        model.HasNoEligibleGroups = eligible.Count == 0;
        model.IsSingleEligibleGroup = eligible.Count == 1;

        var ambiguous = eligible.Count > 1;
        model.EligibleGroups = eligible
            .Select(g => new SelectListItem(
                ambiguous ? $"{g.ProcessName} — {g.GroupName}" : g.ProcessName,
                g.GroupId.ToString(System.Globalization.CultureInfo.InvariantCulture)))
            .ToList();

        if (eligible.Count == 1 && model.GroupId is null)
        {
            model.GroupId = eligible[0].GroupId;
        }
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
                // Spec 035 (evolved 2026-06-16, D14) — attributed impact names + justification.
                AttributedImpactNames = i.AttributedImpactNames,
                ImpactJustification = i.ImpactJustification,
                // Spec 035 / D1 — per-item category field values.
                CategoryFields = i.CategoryFields
                    .Select(cf => new CategoryFieldDisplayViewModel { Label = cf.Label, Value = cf.Value })
                    .ToList(),
                ReviewComment = i.ReviewComment,
                // OR in the legacy English sentinel: pre-fix rows (and rows whose
                // flag was reset while the comment was preserved) still carry it in
                // ReviewComment. Forcing the flag makes the view render the es-CR
                // message and skip the raw English comment branch.
                IsNotTechnicallyEquivalent = i.IsNotTechnicallyEquivalent
                    || FundingPlatform.Web.Helpers.ReviewCommentDisplay.IsLegacyNotEquivalentComment(i.ReviewComment),
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
                    LegacyNeedsReview = q.LegacyNeedsReview,
                    SupplierBranchId = q.SupplierBranchId,
                    ValidUntil = q.ValidUntil,
                    DocumentId = q.DocumentId,
                    DocumentFileName = q.DocumentFileName,
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

        // Spec 035 (evolved 2026-06-16, D16) — the application's declared impacts (app level).
        vm.Impacts = dto.Impacts.Select(ai => new ApplicationImpactDisplayViewModel
        {
            TemplateName = ai.ImpactTemplateName,
            Parameters = ai.ParameterValues.Select(pv => new ImpactParameterDisplayViewModel
            {
                Name = pv.ParameterName,
                DisplayLabel = pv.ParameterDisplayLabel,
                Value = pv.Value ?? string.Empty,
            }).ToList(),
        }).ToList();

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
