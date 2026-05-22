using System.Diagnostics;
using System.Security.Claims;
using FundingPlatform.Application.Services;
using FundingPlatform.Domain.Interfaces;
using FundingPlatform.Infrastructure.Persistence;
using FundingPlatform.Web.Models;
using FundingPlatform.Web.ViewModels.Public;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FundingPlatform.Web.Controllers;

public class HomeController : Controller
{
    // Spec 021 / US7 / T143 / FR-031 — system configuration keys that store the
    // canonical IObjectStorage `ObjectKey.Value` for each public landing slot.
    // The admin upload surface writes them; the public landing reads them to
    // decide between an "Available" link and the *Próximamente* placeholder.
    public const string ReglamentoStorageKeyConfig = "Public.Landing.Reglamento.StorageKey";
    public const string EjemploStorageKeyConfig = "Public.Landing.Ejemplo.StorageKey";

    private readonly IApplicantDashboardProjection _applicantDashboard;
    private readonly AppDbContext _dbContext;
    private readonly ISystemConfigurationRepository _systemConfig;

    public HomeController(
        IApplicantDashboardProjection applicantDashboard,
        AppDbContext dbContext,
        ISystemConfigurationRepository systemConfig)
    {
        _applicantDashboard = applicantDashboard;
        _dbContext = dbContext;
        _systemConfig = systemConfig;
    }

    [AllowAnonymous]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        // Spec 021 / US7 / T143 / FR-031 — anonymous visitors land on the
        // public Programa Semilla page (hero CTA + 3 slot regions + sponsor
        // strip). Authenticated users are redirected to their role-appropriate
        // dashboard so the public page never leaks behind a session.
        if (User?.Identity?.IsAuthenticated == true)
        {
            // Spec 021 — SupplierAdmin-only users (without Admin) land on the
            // suppliers admin index per FR-007 / US3.
            if (User.IsInRole("SupplierAdmin") && !User.IsInRole("Admin"))
            {
                return Redirect("/Admin/Suppliers");
            }

            if (User.IsInRole("Admin"))
            {
                return Redirect("/Admin");
            }

            if (User.IsInRole("Reviewer"))
            {
                return Redirect("/Reviewer/Dashboard");
            }

            // Default: Applicant (every registered user implicitly holds the
            // Applicant role on signup). Render the existing dashboard view so
            // FR-030 *Hola, {Nombre}* greeting continues to fire (spec 011 US1
            // / FR-024 — the dashboard remains the applicant landing surface).
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!string.IsNullOrEmpty(userId))
            {
                var applicant = await _dbContext.Applicants.FirstOrDefaultAsync(a => a.UserId == userId, ct);
                if (applicant is not null)
                {
                    var dto = await _applicantDashboard.GetForUserAsync(applicant.Id, applicant.FirstName, ct);
                    return View("ApplicantDashboard", dto);
                }
            }

            // Authenticated but no Applicant projection — fall back to the
            // applications list so we never serve the public landing to a
            // signed-in user.
            return Redirect("/Application");
        }

        // Anonymous path — assemble the FR-031 slot states.
        var reglamentoConfig = await _systemConfig.GetByKeyAsync(ReglamentoStorageKeyConfig);
        var ejemploConfig = await _systemConfig.GetByKeyAsync(EjemploStorageKeyConfig);

        var vm = new PublicLandingViewModel(
            ReglamentoAvailable: !string.IsNullOrWhiteSpace(reglamentoConfig?.Value),
            EjemploAvailable: !string.IsNullOrWhiteSpace(ejemploConfig?.Value));

        return View(vm);
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
