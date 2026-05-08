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

        string[] allowedRoles = ["Admin", "Reviewer"];
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
}
