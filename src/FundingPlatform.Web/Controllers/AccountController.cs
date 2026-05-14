using FundingPlatform.Domain.Entities;
using FundingPlatform.Infrastructure.Persistence;
using FundingPlatform.Web.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FundingPlatform.Web.Controllers;

public class AccountController : Controller
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly AppDbContext _dbContext;
    private readonly IWebHostEnvironment _environment;

    public AccountController(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        AppDbContext dbContext,
        IWebHostEnvironment environment)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _dbContext = dbContext;
        _environment = environment;
    }

    [HttpGet]
    public IActionResult Register()
    {
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Register(RegisterViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var user = new ApplicationUser
        {
            UserName = model.Email,
            Email = model.Email,
            FirstName = model.FirstName,
            LastName = model.LastName,
        };
        var result = await _userManager.CreateAsync(user, model.Password);

        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error.Description);
            }
            return View(model);
        }

        await _userManager.AddToRoleAsync(user, "Applicant");

        var applicant = new Applicant(
            userId: user.Id,
            legalId: model.LegalId,
            firstName: model.FirstName,
            lastName: model.LastName,
            email: model.Email,
            phone: null,
            performanceScore: null);

        _dbContext.Applicants.Add(applicant);
        await _dbContext.SaveChangesAsync();

        return RedirectToAction(nameof(Login));
    }

    [HttpGet]
    public IActionResult Login()
    {
        return View();
    }

    [HttpGet]
    [AllowAnonymous]
    public IActionResult AccessDenied()
    {
        Response.StatusCode = StatusCodes.Status403Forbidden;
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var result = await _signInManager.PasswordSignInAsync(
            model.Email, model.Password, isPersistent: false, lockoutOnFailure: false);

        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, "Inicio de sesión inválido.");
            return View(model);
        }

        var signedInUser = await _userManager.FindByEmailAsync(model.Email);
        if (signedInUser is { MustChangePassword: true, IsSystemSentinel: false })
        {
            return RedirectToAction(nameof(ChangePassword));
        }

        return RedirectToAction("Index", "Home");
    }

    [Authorize]
    [HttpGet]
    public IActionResult ChangePassword()
    {
        return View(new ChangePasswordViewModel());
    }

    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangePassword(ChangePasswordViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var user = await _userManager.GetUserAsync(User);
        if (user is null)
        {
            return RedirectToAction(nameof(Login));
        }

        var result = await _userManager.ChangePasswordAsync(user, model.OldPassword, model.NewPassword);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error.Description);
            }
            return View(model);
        }

        user.MustChangePassword = false;
        await _userManager.UpdateAsync(user);
        await _userManager.UpdateSecurityStampAsync(user);
        await _signInManager.RefreshSignInAsync(user);

        return RedirectToAction("Index", "Home");
    }

    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await _signInManager.SignOutAsync();
        return RedirectToAction("Index", "Home");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> PromoteToAdmin(string email)
    {
        if (!_environment.IsDevelopment())
        {
            return NotFound();
        }

        var user = await _userManager.FindByEmailAsync(email);
        if (user is null)
        {
            return NotFound();
        }

        if (!await _userManager.IsInRoleAsync(user, "Admin"))
        {
            await _userManager.AddToRoleAsync(user, "Admin");
        }

        return Ok();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> PromoteToReviewer(string email)
    {
        if (!_environment.IsDevelopment())
        {
            return NotFound();
        }

        var user = await _userManager.FindByEmailAsync(email);
        if (user is null)
        {
            return NotFound();
        }

        if (!await _userManager.IsInRoleAsync(user, "Reviewer"))
        {
            await _userManager.AddToRoleAsync(user, "Reviewer");
        }

        return Ok();
    }

    [HttpGet]
    [Route("Account/AssignRole")]
    public async Task<IActionResult> AssignRole(string email, string role)
    {
        if (!_environment.IsDevelopment())
        {
            return NotFound();
        }

        // Spec 021 / US3 / T103 — E2E provisioning supports the new
        // SupplierAdmin role so US3 can drive a real "role assigned" path
        // through the admin UI it would normally use in prod.
        string[] allowedRoles = ["Admin", "Reviewer", "SupplierAdmin"];
        if (!allowedRoles.Contains(role))
        {
            return BadRequest("Invalid role.");
        }

        var user = await _userManager.FindByEmailAsync(email);
        if (user is null)
        {
            return NotFound();
        }

        if (!await _userManager.IsInRoleAsync(user, role))
        {
            await _userManager.AddToRoleAsync(user, role);
        }

        // Spec 016 — the Admin role MUST never carry group memberships
        // (FR-008, Group.cs invariant). E2E tests register an admin and call
        // AssignRole(role=Admin); without this strip, the
        // RegisterUserAsync→AssignAllGroups default leaves stale memberships
        // attached, which is observable on /Admin/Users/{id}/Edit (group
        // selector pre-populated for an Admin role) and contradicts the
        // documented invariant. The strip is idempotent.
        if (string.Equals(role, "Admin", StringComparison.Ordinal))
        {
            var existingMemberships = _dbContext.UserGroupMemberships
                .Where(m => m.UserId == user.Id);
            _dbContext.UserGroupMemberships.RemoveRange(existingMemberships);
            await _dbContext.SaveChangesAsync();
        }

        return Ok($"Role '{role}' assigned to '{email}'.");
    }

    /// <summary>
    /// Spec 016 — dev-only helper for the E2E suite. Assigns the user to every
    /// row in <c>dbo.Groups</c> (idempotent: already-assigned rows are skipped).
    /// Existing E2E tests register applicants/reviewers via
    /// <c>RegisterUserAsync</c> + <c>AssignRoleAsync</c> and never touched the
    /// group catalog; spec 016's reviewer-side group-overlap predicate would
    /// otherwise hide every applicant from every reviewer that lacks a
    /// membership. The new tests for spec 016 use the admin UI to seed
    /// memberships explicitly and do NOT call this endpoint.
    /// </summary>
    [HttpGet]
    [Route("Account/AssignAllGroups")]
    public async Task<IActionResult> AssignAllGroups(string email)
    {
        if (!_environment.IsDevelopment())
        {
            return NotFound();
        }

        var user = await _userManager.FindByEmailAsync(email);
        if (user is null)
        {
            return NotFound();
        }

        var allGroupIds = await _dbContext.Groups
            .Select(g => g.Id)
            .ToListAsync();

        if (allGroupIds.Count == 0)
        {
            return Ok($"No groups exist; nothing assigned to '{email}'.");
        }

        var existing = await _dbContext.UserGroupMemberships
            .Where(m => m.UserId == user.Id)
            .Select(m => m.GroupId)
            .ToListAsync();

        var toAdd = allGroupIds.Except(existing).ToList();
        foreach (var gid in toAdd)
        {
            _dbContext.UserGroupMemberships.Add(new Domain.Entities.UserGroupMembership(user.Id, gid));
        }

        if (toAdd.Count > 0)
        {
            await _dbContext.SaveChangesAsync();
        }

        return Ok($"Assigned {toAdd.Count} group(s) to '{email}'.");
    }

    /// <summary>
    /// Spec 017 — dev-only helper that brings the admin-relevant slice of the DB
    /// to a "zero of everything" state expected by US1 / US3 / US7 E2E tests.
    /// The shared <see cref="AspireFixture"/> accumulates state across the suite,
    /// so without this reset the spec-017 zero-fixture assertions race against
    /// suppliers, audit events, groups, and impact templates created by earlier
    /// tests (and the dacpac post-deploy seed).
    /// Suppliers cannot be deleted (FK from <c>Items.SelectedSupplierId</c> +
    /// <c>Quotations.SupplierId</c> are NO ACTION) so they are flipped to
    /// <c>Verified</c> + fully compliant, which makes both the default
    /// <c>PendingReview</c> filter and the <c>?hasIncompleteCompliance=true</c>
    /// filter return zero rows. Pair with <see cref="SeedAdminFixture"/> in a
    /// test teardown so subsequent tests (reviewer-queue group-overlap predicate,
    /// applicant flow that needs a seeded ImpactTemplate) keep working.
    /// </summary>
    [HttpGet]
    [Route("Account/ResetAdminFixture")]
    public async Task<IActionResult> ResetAdminFixture()
    {
        if (!_environment.IsDevelopment())
        {
            return NotFound();
        }

        await _dbContext.Database.ExecuteSqlRawAsync("DELETE FROM dbo.AdminAuditEvents;");
        await _dbContext.Database.ExecuteSqlRawAsync(
            "UPDATE dbo.Suppliers SET VerificationStatus = 2, IsCompliantCCSS = 1, IsCompliantHacienda = 1, IsCompliantSICOP = 1, HasElectronicInvoice = 1;");
        await _dbContext.Database.ExecuteSqlRawAsync(
            "UPDATE dbo.Quotations SET LegacyNeedsReview = 0 WHERE LegacyNeedsReview = 1;");
        // UserGroupMemberships → Groups: ON DELETE CASCADE wipes memberships.
        await _dbContext.Database.ExecuteSqlRawAsync("DELETE FROM dbo.Groups;");
        // ImpactTemplates referenced by Impacts (NO ACTION) and ImpactParameterValues
        // via ImpactTemplateParameters (NO ACTION); CASCADE only covers the
        // template-to-parameter direction.
        await _dbContext.Database.ExecuteSqlRawAsync("DELETE FROM dbo.ImpactParameterValues;");
        await _dbContext.Database.ExecuteSqlRawAsync("DELETE FROM dbo.Impacts;");
        await _dbContext.Database.ExecuteSqlRawAsync("DELETE FROM dbo.ImpactTemplates;");

        return Ok("Admin fixture reset.");
    }

    /// <summary>
    /// Spec 017 — dev-only companion to <see cref="ResetAdminFixture"/> that
    /// re-plants the post-deploy seed rows the rest of the E2E suite depends on:
    /// the three demo Groups (Norte / Sur / Centro) used by the spec 016
    /// reviewer group-overlap predicate, and the two ImpactTemplates +
    /// parameters consumed by <c>PickFirstImpactTemplateAsync</c> in the
    /// applicant journey. Idempotent — skips rows that already exist by name.
    /// </summary>
    [HttpGet]
    [Route("Account/SeedAdminFixture")]
    public async Task<IActionResult> SeedAdminFixture()
    {
        if (!_environment.IsDevelopment())
        {
            return NotFound();
        }

        await _dbContext.Database.ExecuteSqlRawAsync(@"
MERGE INTO dbo.Groups AS tgt
USING (VALUES (N'Norte'), (N'Sur'), (N'Centro')) AS src ([Name])
ON tgt.[Name] = src.[Name]
WHEN NOT MATCHED THEN INSERT ([Name], [CreatedAt], [UpdatedAt])
    VALUES (src.[Name], SYSUTCDATETIME(), SYSUTCDATETIME());");

        await _dbContext.Database.ExecuteSqlRawAsync(@"
IF NOT EXISTS (SELECT 1 FROM dbo.ImpactTemplates WHERE [Name] = N'Increase Production Capacity')
BEGIN
    INSERT INTO dbo.ImpactTemplates ([Name], [Description], [IsActive], [UpdatedAt])
    VALUES (N'Increase Production Capacity',
            N'Measures the expected increase in production capacity resulting from the funded item',
            1, GETUTCDATE());
    DECLARE @cap INT = SCOPE_IDENTITY();
    INSERT INTO dbo.ImpactTemplateParameters ([ImpactTemplateId], [Name], [DisplayLabel], [DataType], [IsRequired], [SortOrder])
    VALUES
        (@cap, N'CurrentCapacity',    N'Current Capacity',    1, 1, 1),
        (@cap, N'ProjectedCapacity',  N'Projected Capacity',  1, 1, 2),
        (@cap, N'TimeframeInMonths',  N'Timeframe in Months', 2, 1, 3);
END;
IF NOT EXISTS (SELECT 1 FROM dbo.ImpactTemplates WHERE [Name] = N'Job Creation')
BEGIN
    INSERT INTO dbo.ImpactTemplates ([Name], [Description], [IsActive], [UpdatedAt])
    VALUES (N'Job Creation',
            N'Measures the expected number of new jobs created as a result of the funded item',
            1, GETUTCDATE());
    DECLARE @job INT = SCOPE_IDENTITY();
    INSERT INTO dbo.ImpactTemplateParameters ([ImpactTemplateId], [Name], [DisplayLabel], [DataType], [IsRequired], [SortOrder])
    VALUES
        (@job, N'CurrentEmployees',  N'Current Employees',   2, 1, 1),
        (@job, N'ProjectedNewJobs',  N'Projected New Jobs',  2, 1, 2),
        (@job, N'JobType',           N'Job Type',            0, 1, 3);
END;");

        return Ok("Admin fixture re-seeded.");
    }

    /// <summary>
    /// Spec 021 / T114 / US4 — dev-only helper for the stage-expiry E2E test.
    /// Backdates <c>Applications.StageEnteredAt</c> by the supplied
    /// <paramref name="daysAgo"/> so the test can drive an Application into the
    /// "Vencido" bucket without waiting for real time to pass. Idempotent;
    /// production environments return 404.
    /// </summary>
    [HttpGet]
    [Route("Account/BackdateStageEntered")]
    public async Task<IActionResult> BackdateStageEntered(int applicationId, int daysAgo)
    {
        if (!_environment.IsDevelopment())
        {
            return NotFound();
        }
        if (daysAgo <= 0) return BadRequest("daysAgo must be positive.");

        var newInstant = DateTimeOffset.UtcNow.AddDays(-daysAgo);
        var rows = await _dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE dbo.Applications SET StageEnteredAt = {newInstant} WHERE Id = {applicationId};");
        return Ok($"Backdated Application {applicationId} StageEnteredAt by {daysAgo} day(s); rows={rows}.");
    }
}
