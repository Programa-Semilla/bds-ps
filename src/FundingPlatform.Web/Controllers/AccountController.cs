using FundingPlatform.Application.Abstractions;
using FundingPlatform.Application.Identity;
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
    private readonly IIssuePasswordResetTokenHandler _issuePasswordResetTokenHandler;
    private readonly IConsumePasswordResetTokenHandler _consumePasswordResetTokenHandler;
    private readonly IUpdateProfileHandler _updateProfileHandler;
    private readonly IEmailSender _emailSender;
    private readonly Infrastructure.Email.ForgotPasswordEmailFactory _forgotPasswordEmailFactory;

    public AccountController(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        AppDbContext dbContext,
        IWebHostEnvironment environment,
        IIssuePasswordResetTokenHandler issuePasswordResetTokenHandler,
        IConsumePasswordResetTokenHandler consumePasswordResetTokenHandler,
        IUpdateProfileHandler updateProfileHandler,
        IEmailSender emailSender,
        Infrastructure.Email.ForgotPasswordEmailFactory forgotPasswordEmailFactory)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _dbContext = dbContext;
        _environment = environment;
        _issuePasswordResetTokenHandler = issuePasswordResetTokenHandler;
        _consumePasswordResetTokenHandler = consumePasswordResetTokenHandler;
        _updateProfileHandler = updateProfileHandler;
        _emailSender = emailSender;
        _forgotPasswordEmailFactory = forgotPasswordEmailFactory;
    }

    // Spec 032 — public self-registration removed. Accounts are created only by an
    // administrator via /Admin/Users/Create (which creates the ApplicationUser + Applicant).
    // The former Register GET/POST actions are gone, so /Account/Register returns 404.

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

    // -----------------------------------------------------------------------
    // Spec 021 / US5 / T127 / FR-028 — Forgot-password flow.
    // The POST always returns the same neutral view regardless of whether the
    // email is on file (no enumeration). When the email is known, an email is
    // dispatched out-of-band; when unknown, no email is sent. Both branches
    // render the same view, set the same TempData success banner, and return
    // 200 — the response is indistinguishable to the client.
    // -----------------------------------------------------------------------

    [HttpGet]
    [AllowAnonymous]
    public IActionResult ForgotPassword()
    {
        return View(new ForgotPasswordViewModel());
    }

    [HttpPost]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordViewModel model, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var result = await _issuePasswordResetTokenHandler.HandleAsync(
            new IssuePasswordResetTokenCommand(model.Email), ct);

        if (result.UserFound && !string.IsNullOrEmpty(result.RawToken) && !string.IsNullOrEmpty(result.UserId))
        {
            // Compose absolute reset link. Token is opaque base-64 from
            // DataProtectorTokenProvider — URL-encode it so '+', '/', '=' survive.
            var resetLink = Url.Action(
                action: nameof(ResetPassword),
                controller: "Account",
                values: new { userId = result.UserId, token = result.RawToken },
                protocol: Request.Scheme,
                host: Request.Host.Value);

            if (!string.IsNullOrEmpty(resetLink))
            {
                var expiresAt = DateTimeOffset.UtcNow.Add(PasswordResetToken.DefaultLifetime);
                var envelope = _forgotPasswordEmailFactory.Build(
                    toAddress: result.Email!,
                    applicantFirstName: result.FirstName,
                    resetLink: resetLink,
                    expiresAt: expiresAt);
                try
                {
                    await _emailSender.SendAsync(envelope, ct);
                }
                catch (Exception ex)
                {
                    // Swallow transport errors here — the neutral response is
                    // required by FR-028 (no enumeration). The error is logged
                    // by the sender; we MUST NOT surface it to the client.
                    HttpContext.RequestServices
                        .GetRequiredService<ILogger<AccountController>>()
                        .LogWarning(ex, "Failed to send password-reset email; rendering neutral response anyway.");
                }
            }
        }

        // Neutral response for BOTH branches.
        TempData["SuccessMessage"] =
            "Si la dirección está registrada, le enviaremos instrucciones para restablecer su contraseña.";
        return View(new ForgotPasswordViewModel { Email = model.Email });
    }

    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> ResetPassword(string? userId, string? token)
    {
        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(token))
        {
            ViewData["InvalidLink"] = true;
            return View(new ResetPasswordViewModel());
        }

        // Soft existence check — the heavy verification happens on POST. We
        // only need the user to exist; if they don't, render the invalid-link
        // view so the form isn't presented.
        var user = await _userManager.FindByIdAsync(userId);
        if (user is null)
        {
            ViewData["InvalidLink"] = true;
            return View(new ResetPasswordViewModel());
        }

        return View(new ResetPasswordViewModel
        {
            UserId = userId,
            Token = token,
        });
    }

    [HttpPost]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResetPassword(ResetPasswordViewModel model, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var result = await _consumePasswordResetTokenHandler.HandleAsync(
            new ConsumePasswordResetTokenCommand(model.UserId, model.Token, model.NewPassword), ct);
        if (!result.Success)
        {
            foreach (var msg in result.ErrorMessages)
            {
                ModelState.AddModelError(string.Empty, msg);
            }
            return View(model);
        }

        TempData["SuccessMessage"] = "Contraseña actualizada. Inicie sesión con su nueva contraseña.";
        return RedirectToAction(nameof(Login));
    }

    // -----------------------------------------------------------------------
    // Spec 021 / US5 / T127 / FR-018 — Self-service profile.
    // FirstName / LastName / Phone / Address are editable. Email / Role /
    // Group / CodigoPersonal render as read-only "administrado" fields and
    // are rebuilt server-side on every request, so smuggled form fields
    // cannot reach UpdateProfileCommand.
    // -----------------------------------------------------------------------

    [Authorize]
    [HttpGet]
    [Route("Profile")]
    public async Task<IActionResult> Profile()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null)
        {
            return RedirectToAction(nameof(Login));
        }

        var vm = await BuildProfileViewModelAsync(user);
        return View(vm);
    }

    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Route("Profile/Update")]
    public async Task<IActionResult> ProfileUpdate(ProfileViewModel model, CancellationToken ct)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null)
        {
            return RedirectToAction(nameof(Login));
        }

        // Re-validate just the four self-editable fields; do not rely on the
        // view-model's read-only properties (they are admin-managed).
        ModelState.Remove(nameof(ProfileViewModel.Email));
        ModelState.Remove(nameof(ProfileViewModel.Role));
        ModelState.Remove(nameof(ProfileViewModel.Group));
        ModelState.Remove(nameof(ProfileViewModel.CodigoPersonal));
        ModelState.Remove($"{nameof(ProfileViewModel.ChangePassword)}.{nameof(ChangePasswordViewModel.OldPassword)}");
        ModelState.Remove($"{nameof(ProfileViewModel.ChangePassword)}.{nameof(ChangePasswordViewModel.NewPassword)}");
        ModelState.Remove($"{nameof(ProfileViewModel.ChangePassword)}.{nameof(ChangePasswordViewModel.ConfirmPassword)}");

        if (!ModelState.IsValid)
        {
            var rebuilt = await BuildProfileViewModelAsync(user);
            rebuilt.FirstName = model.FirstName;
            rebuilt.LastName = model.LastName;
            rebuilt.Phone = model.Phone;
            rebuilt.Address = model.Address;
            return View("Profile", rebuilt);
        }

        var result = await _updateProfileHandler.HandleAsync(
            new UpdateProfileCommand(user.Id, model.FirstName, model.LastName, model.Phone, model.Address), ct);
        if (!result.Success)
        {
            foreach (var msg in result.ErrorMessages)
            {
                ModelState.AddModelError(string.Empty, msg);
            }
            var rebuilt = await BuildProfileViewModelAsync(user);
            rebuilt.FirstName = model.FirstName;
            rebuilt.LastName = model.LastName;
            rebuilt.Phone = model.Phone;
            rebuilt.Address = model.Address;
            return View("Profile", rebuilt);
        }

        TempData["SuccessMessage"] = "Perfil actualizado.";
        return RedirectToAction(nameof(Profile));
    }

    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Route("Profile/ChangePassword")]
    public async Task<IActionResult> ProfileChangePassword(ProfileViewModel model, CancellationToken ct)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null)
        {
            return RedirectToAction(nameof(Login));
        }

        // Validate just the password section; profile-edit fields stay put.
        ModelState.Remove(nameof(ProfileViewModel.FirstName));
        ModelState.Remove(nameof(ProfileViewModel.LastName));
        ModelState.Remove(nameof(ProfileViewModel.Phone));
        ModelState.Remove(nameof(ProfileViewModel.Address));
        ModelState.Remove(nameof(ProfileViewModel.Email));
        ModelState.Remove(nameof(ProfileViewModel.Role));
        ModelState.Remove(nameof(ProfileViewModel.Group));
        ModelState.Remove(nameof(ProfileViewModel.CodigoPersonal));

        if (!ModelState.IsValid)
        {
            var rebuilt = await BuildProfileViewModelAsync(user);
            rebuilt.ChangePassword = model.ChangePassword;
            return View("Profile", rebuilt);
        }

        var result = await _userManager.ChangePasswordAsync(
            user, model.ChangePassword.OldPassword, model.ChangePassword.NewPassword);
        if (!result.Succeeded)
        {
            foreach (var err in result.Errors)
            {
                ModelState.AddModelError(string.Empty, err.Description);
            }
            var rebuilt = await BuildProfileViewModelAsync(user);
            rebuilt.ChangePassword = model.ChangePassword;
            return View("Profile", rebuilt);
        }

        user.MustChangePassword = false;
        await _userManager.UpdateAsync(user);
        await _userManager.UpdateSecurityStampAsync(user);

        // Force re-login per spec (security-stamp refresh invalidates the
        // cookie anyway; doing the sign-out makes the redirect target obvious).
        await _signInManager.SignOutAsync();
        TempData["SuccessMessage"] = "Contraseña actualizada. Inicie sesión con su nueva contraseña.";
        return RedirectToAction(nameof(Login));
    }

    private async Task<ProfileViewModel> BuildProfileViewModelAsync(ApplicationUser user)
    {
        var roles = await _userManager.GetRolesAsync(user);
        // Highest-rank role first — Admin > Reviewer > SupplierAdmin > Applicant.
        string DisplayRole(string r) => r switch
        {
            "Admin" => "Administrador",
            "Reviewer" => "Revisor",
            "SupplierAdmin" => "Administrador de proveedores",
            "Applicant" => "Solicitante",
            _ => r,
        };
        var rolePriority = new[] { "Admin", "Reviewer", "SupplierAdmin", "Applicant" };
        var roleLabel = rolePriority.FirstOrDefault(roles.Contains);

        // Pull groups via the same query that AdminUsersController uses.
        var groups = await _dbContext.UserGroupMemberships
            .AsNoTracking()
            .Where(m => m.UserId == user.Id)
            .Join(_dbContext.Groups.AsNoTracking(), m => m.GroupId, g => g.Id, (m, g) => g.Name)
            .OrderBy(n => n)
            .ToListAsync();
        var groupLabel = groups.Count == 0 ? "—" : string.Join(", ", groups);

        // Address is stored as a user-claim (see UpdateProfileHandler).
        var claims = await _userManager.GetClaimsAsync(user);
        var address = claims.FirstOrDefault(c => c.Type == "profile.address")?.Value;

        // Spec 026 — identification is admin-managed; surface it read-only.
        var applicant = await _dbContext.Applicants
            .AsNoTracking()
            .Where(a => a.UserId == user.Id)
            .Select(a => new { a.LegalId, a.IdentificationType, a.UserCode })
            .FirstOrDefaultAsync();

        return new ProfileViewModel
        {
            FirstName = user.FirstName,
            LastName = user.LastName,
            Phone = user.PhoneNumber,
            Address = address,
            Email = user.Email ?? "",
            Role = roleLabel is null ? "—" : DisplayRole(roleLabel),
            Group = groupLabel,
            CodigoPersonal = user.CodigoPersonal,
            LegalId = applicant?.LegalId,
            IdentificationType = applicant?.IdentificationType,
            UserCode = applicant?.UserCode,
            IsApplicant = string.Equals(roleLabel, "Applicant", StringComparison.Ordinal),
        };
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
    /// Spec 032 — dev-only E2E provisioning seam. Public self-registration was
    /// removed (FR-001/FR-002), but the E2E suite bootstraps its test users
    /// through what used to be the Register form. This endpoint reproduces the
    /// former Register POST exactly (create the ApplicationUser, add the
    /// Applicant role, create the companion Applicant with a Cédula física),
    /// gated to Development like the other dev seams. It is NOT a public
    /// registration path: it is unreachable outside Development (404) and has no
    /// UI. Product user creation remains admin-only via /Admin/Users/Create.
    /// </summary>
    [HttpGet]
    [Route("Account/SeedUser")]
    [AllowAnonymous]
    public async Task<IActionResult> SeedUser(string email, string password, string firstName, string lastName, string legalId)
    {
        if (!_environment.IsDevelopment())
        {
            return NotFound();
        }

        var existing = await _userManager.FindByEmailAsync(email);
        if (existing is not null)
        {
            return Ok($"User '{email}' already exists.");
        }

        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            FirstName = firstName,
            LastName = lastName,
        };
        var result = await _userManager.CreateAsync(user, password);
        if (!result.Succeeded)
        {
            return BadRequest(string.Join("; ", result.Errors.Select(e => e.Description)));
        }

        await _userManager.AddToRoleAsync(user, "Applicant");

        _dbContext.Applicants.Add(new Applicant(
            userId: user.Id,
            legalId: legalId,
            firstName: firstName,
            lastName: lastName,
            email: email,
            phone: null,
            performanceScore: null,
            identificationType: Domain.Enums.IdentificationType.CedulaFisica));
        await _dbContext.SaveChangesAsync();

        return Ok($"User '{email}' seeded.");
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

        // Spec 029 — Applications now carry a NOT NULL FK to Groups
        // (Applications.GroupId), so the Groups delete below fails unless the
        // referencing Applications are removed first. Wipe the full Application
        // subtree in FK-safe order (children → parents). This also zeroes the
        // dashboard KPIs the ZeroOfEverything fixture asserts on.
        foreach (var table in new[]
        {
            "SigningReviewDecisions", "SignedUploads", "FundingAgreements",
            "AppealMessages", "Appeals", "ItemResponses", "ApplicantResponses",
            "ComparisonArtifacts", "ComparisonJobs", "Quotations", "Items",
            // NotificationOutbox.VersionHistoryId → VersionHistory (NO ACTION),
            // and NotificationDelivery → NotificationOutbox, so delete deliveries,
            // then outbox, then VersionHistory.
            "NotificationDelivery", "NotificationOutbox", "VersionHistory",
            "ImpactParameterValues", "Applications",
        })
        {
            await _dbContext.Database.ExecuteSqlRawAsync($"DELETE FROM dbo.{table};");
        }

        // UserGroupMemberships → Groups: ON DELETE CASCADE wipes memberships.
        // Spec 021-feedback-session-may13 — Groups carry a NOT NULL FK to
        // Processes. Deleting Groups does not touch Processes (child→parent),
        // so the existing DELETE is safe. We leave the "Migración inicial"
        // Process and any Plantillas in place; SeedAdminFixture re-attaches
        // the demo groups to it on the next teardown call.
        await _dbContext.Database.ExecuteSqlRawAsync("DELETE FROM dbo.Groups;");
        // Spec 021-feedback-session-may13 — dbo.Impacts table dropped (FR-005;
        // Impact relocated from Item to Application as a value object). The
        // dependent ImpactParameterValues now reference Applications, not
        // Impacts, and survive the template reset.
        await _dbContext.Database.ExecuteSqlRawAsync("DELETE FROM dbo.ImpactParameterValues;");
        // Spec 021-feedback-session-may13 — new NO-ACTION FKs into
        // ImpactTemplates: Applications.ImpactTemplateId (nullable) and
        // PlantillaImpactTemplates.ImpactTemplateId. Null the Applications ref
        // and wipe the join rows before deleting templates.
        await _dbContext.Database.ExecuteSqlRawAsync(
            "UPDATE dbo.Applications SET ImpactTemplateId = NULL WHERE ImpactTemplateId IS NOT NULL;");
        await _dbContext.Database.ExecuteSqlRawAsync("DELETE FROM dbo.PlantillaImpactTemplates;");
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

        // Spec 021-feedback-session-may13 / data-model — Groups.ProcessId is
        // NOT NULL with a real FK to dbo.Processes. The 02_SeedMigracionInicial
        // post-deploy script provides the bootstrap row; resolve it here and
        // attach the demo groups to it so the insert satisfies the FK.
        // Spec 029 — Processes.FundId is a required FK; resolve the seed
        // "Fondo General" Fund (dacpac post-deploy plants it; the reset leaves
        // Funds in place) so a re-inserted Process satisfies the constraint.
        await _dbContext.Database.ExecuteSqlRawAsync(@"
DECLARE @FundId INT = (SELECT TOP 1 [Id] FROM [dbo].[Funds] WHERE [Name] = N'Fondo General' ORDER BY [Id]);
IF @FundId IS NULL
BEGIN
    INSERT INTO [dbo].[Funds] ([Name], [Description], [Status]) VALUES (N'Fondo General', N'Fondo general del Programa Semilla.', 0);
    SET @FundId = SCOPE_IDENTITY();
END;
DECLARE @ProcessId INT = (
    SELECT [Id] FROM [dbo].[Processes] WHERE [Name] = N'Migración inicial'
);
IF @ProcessId IS NULL
BEGIN
    INSERT INTO [dbo].[Processes] ([Name], [Status], [FundId]) VALUES (N'Migración inicial', 0, @FundId);
    SET @ProcessId = SCOPE_IDENTITY();
END;
MERGE INTO dbo.Groups AS tgt
USING (VALUES (N'Norte'), (N'Sur'), (N'Centro')) AS src ([Name])
ON tgt.[Name] = src.[Name]
WHEN NOT MATCHED THEN INSERT ([Name], [ProcessId], [CreatedAt], [UpdatedAt])
    VALUES (src.[Name], @ProcessId, SYSUTCDATETIME(), SYSUTCDATETIME());");

        await _dbContext.Database.ExecuteSqlRawAsync(@"
IF NOT EXISTS (SELECT 1 FROM dbo.ImpactTemplates WHERE [Name] = N'Aumento de capacidad productiva')
BEGIN
    INSERT INTO dbo.ImpactTemplates ([Name], [Description], [IsActive], [UpdatedAt])
    VALUES (N'Aumento de capacidad productiva',
            N'Mide el aumento esperado en la capacidad productiva como resultado del bien adquirido',
            1, GETUTCDATE());
    DECLARE @cap INT = SCOPE_IDENTITY();
    INSERT INTO dbo.ImpactTemplateParameters ([ImpactTemplateId], [Name], [DisplayLabel], [DataType], [IsRequired], [SortOrder])
    VALUES
        (@cap, N'CurrentCapacity',    N'Capacidad actual',     1, 1, 1),
        (@cap, N'ProjectedCapacity',  N'Capacidad proyectada', 1, 1, 2),
        (@cap, N'TimeframeInMonths',  N'Plazo en meses',       2, 1, 3);
END;
IF NOT EXISTS (SELECT 1 FROM dbo.ImpactTemplates WHERE [Name] = N'Generación de empleo')
BEGIN
    INSERT INTO dbo.ImpactTemplates ([Name], [Description], [IsActive], [UpdatedAt])
    VALUES (N'Generación de empleo',
            N'Mide la cantidad esperada de nuevos empleos generados como resultado del bien adquirido',
            1, GETUTCDATE());
    DECLARE @job INT = SCOPE_IDENTITY();
    INSERT INTO dbo.ImpactTemplateParameters ([ImpactTemplateId], [Name], [DisplayLabel], [DataType], [IsRequired], [SortOrder])
    VALUES
        (@job, N'CurrentEmployees',  N'Personas empleadas actuales', 2, 1, 1),
        (@job, N'ProjectedNewJobs',  N'Nuevos empleos proyectados',  2, 1, 2),
        (@job, N'JobType',           N'Tipo de empleo',              0, 1, 3);
END;");

        return Ok("Admin fixture re-seeded.");
    }

    /// <summary>
    /// Spec 021 / T124 / US5 — dev-only helper for the forgot-password E2E
    /// test. Returns the latest *unconsumed* password-reset link for the user
    /// matching <paramref name="email"/>. Returns 404 outside development.
    /// The link is composed exactly the way the controller's email path
    /// composes it, so the E2E test can follow it like a real user would.
    /// </summary>
    [HttpGet]
    [Route("Account/LatestPasswordResetLink")]
    public async Task<IActionResult> LatestPasswordResetLink(string email)
    {
        if (!_environment.IsDevelopment())
        {
            return NotFound();
        }
        if (string.IsNullOrWhiteSpace(email))
        {
            return BadRequest("email is required.");
        }

        var user = await _userManager.FindByEmailAsync(email);
        if (user is null)
        {
            return NotFound();
        }

        var rawToken = await _userManager.GeneratePasswordResetTokenAsync(user);
        await _dbContext.Set<PasswordResetToken>().AddAsync(
            PasswordResetToken.Issue(
                user.Id,
                System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(rawToken)),
                DateTimeOffset.UtcNow,
                PasswordResetToken.DefaultLifetime));
        await _dbContext.SaveChangesAsync();

        var link = Url.Action(
            action: nameof(ResetPassword),
            controller: "Account",
            values: new { userId = user.Id, token = rawToken },
            protocol: Request.Scheme,
            host: Request.Host.Value);

        return Ok(link);
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

    /// <summary>
    /// Spec 021 / T151 / T154 / US8 / FR-021 — dev-only helper for the
    /// soft-delete E2E regression. Loads the Application, invokes the domain
    /// <see cref="Domain.Entities.Application.SoftDelete"/> method, and saves.
    /// Mirrors what the admin <c>POST /Admin/Applications/{id}/SoftDelete</c>
    /// route does so the E2E can drive it without antiforgery / admin-cookie
    /// plumbing. Production environments return 404.
    /// </summary>
    [HttpGet]
    [Route("Account/SoftDeleteApplication")]
    public async Task<IActionResult> SoftDeleteApplication(int applicationId)
    {
        if (!_environment.IsDevelopment())
        {
            return NotFound();
        }

        var entity = await _dbContext.Applications
            .FirstOrDefaultAsync(a => a.Id == applicationId);
        if (entity is null)
        {
            return NotFound($"Application {applicationId} not found.");
        }

        entity.SoftDelete();
        await _dbContext.SaveChangesAsync();
        return Ok($"Soft-deleted Application {applicationId}; DeletedAt={entity.DeletedAt:o}.");
    }
}
