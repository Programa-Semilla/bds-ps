using System.Security.Cryptography;
using FundingPlatform.Domain.Entities;
using FundingPlatform.Domain.Enums;
using FundingPlatform.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FundingPlatform.Infrastructure.Identity;

public static class IdentityConfiguration
{
    public const string SentinelEmail = "admin@programa-semilla.test";
    public const string SentinelPasswordConfigKey = "Admin:DefaultPassword";

    public static async Task SeedRolesAsync(IServiceProvider serviceProvider)
    {
        var roleManager = serviceProvider.GetRequiredService<RoleManager<IdentityRole>>();

        // Spec 038 — Auditor (renamed from SupplierAdmin) included. The dacpac
        // post-deployment script `03_SeedSupplierAdminRole.sql` is the canonical
        // rename-or-create in deployed envs; this C# branch covers paths that bypass
        // the dacpac (in-memory tests, fresh demos) so the role row is always present
        // when seed users try to enrol into it.
        // Spec 045 — Financial Operator (group-scoped, like Reviewer/Auditor). The dacpac
        // post-deployment `10_SeedFinancialOperatorRole.sql` is canonical in deployed envs;
        // this C# branch covers dacpac-bypass paths (in-memory tests, fresh demos).
        string[] roles = ["Applicant", "Admin", "Reviewer", "Auditor", "Financial Operator"];

        foreach (var role in roles)
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                await roleManager.CreateAsync(new IdentityRole(role));
            }
        }
    }

    public static async Task SeedSentinelAdminAsync(
        IServiceProvider serviceProvider,
        IConfiguration configuration,
        ILogger logger)
    {
        var userManager = serviceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        var existing = await userManager.FindByEmailAsync(SentinelEmail);
        if (existing is { IsSystemSentinel: true })
        {
            return;
        }
        if (existing is not null)
        {
            logger.LogWarning(
                "User '{Email}' exists but is not flagged as system sentinel; sentinel seeding is skipped to avoid double-seeding.",
                SentinelEmail);
            return;
        }

        var configured = configuration[SentinelPasswordConfigKey];
        var generated = string.IsNullOrWhiteSpace(configured);
        var password = generated
            ? Convert.ToBase64String(RandomNumberGenerator.GetBytes(24))
            : configured!;

        if (generated)
        {
            logger.LogWarning(
                "Sentinel admin '{Email}' will be created with auto-generated password: {Password}",
                SentinelEmail, password);
        }

        var sentinel = ApplicationUser.CreateSentinel(SentinelEmail);
        var createResult = await userManager.CreateAsync(sentinel, password);
        if (!createResult.Succeeded)
        {
            logger.LogError(
                "Sentinel admin '{Email}' creation failed: {Errors}",
                SentinelEmail,
                string.Join("; ", createResult.Errors.Select(e => $"{e.Code}: {e.Description}")));
            return;
        }

        var roleResult = await userManager.AddToRoleAsync(sentinel, "Admin");
        if (!roleResult.Succeeded)
        {
            logger.LogError(
                "Sentinel admin '{Email}' role assignment failed: {Errors}",
                SentinelEmail,
                string.Join("; ", roleResult.Errors.Select(e => $"{e.Code}: {e.Description}")));
        }
    }

    public static async Task SeedUsersAsync(IServiceProvider serviceProvider)
    {
        var userManager = serviceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var dbContext = serviceProvider.GetRequiredService<AppDbContext>();

        // Spec 026 — demo applicants carry valid canonical cédulas físicas + type.
        var seedUsers = new[]
        {
            new { Email = "applicant@programa-semilla.test", Password = "Demo123!", FirstName = "Ana", LastName = "Pérez", LegalId = "1-0001-0001", IdType = IdentificationType.CedulaFisica, Roles = new[] { "Applicant" } },
            new { Email = "reviewer@programa-semilla.test", Password = "Demo123!", FirstName = "Carlos", LastName = "Rivera", LegalId = "1-0001-0002", IdType = IdentificationType.CedulaFisica, Roles = new[] { "Reviewer" } },
            new { Email = "demo-admin@programa-semilla.test", Password = "Demo123!", FirstName = "María", LastName = "Torres", LegalId = "1-0001-0003", IdType = IdentificationType.CedulaFisica, Roles = new[] { "Admin" } },
            // Spec 038 — demo Auditor so the supplier-catalog-only sidebar variant
            // is reachable from a one-click login in dev/E2E without having to
            // provision via /Account/AssignRole first.
            new { Email = "auditor@programa-semilla.test", Password = "Demo123!", FirstName = "Lucía", LastName = "Mora", LegalId = "1-0001-0004", IdType = IdentificationType.CedulaFisica, Roles = new[] { "Auditor" } },
        };

        foreach (var seed in seedUsers)
        {
            if (await userManager.FindByEmailAsync(seed.Email) is not null)
                continue;

            var user = new ApplicationUser(seed.Email, seed.FirstName, seed.LastName, phone: null);
            var result = await userManager.CreateAsync(user, seed.Password);
            if (!result.Succeeded)
                continue;

            foreach (var role in seed.Roles)
            {
                await userManager.AddToRoleAsync(user, role);
            }

            // Create Applicant record so the user can interact with the system
            if (!await dbContext.Applicants.AnyAsync(a => a.UserId == user.Id))
            {
                dbContext.Applicants.Add(new Applicant(
                    userId: user.Id,
                    legalId: seed.LegalId,
                    firstName: seed.FirstName,
                    lastName: seed.LastName,
                    email: seed.Email,
                    phone: null,
                    performanceScore: null,
                    identificationType: seed.IdType));
            }
        }

        await dbContext.SaveChangesAsync();

        // Spec 029 / T024 — anchor the demo applicant (and the demo reviewer, for
        // queue overlap) to a Group under the seed Fund's Active Process so the
        // application-create flow auto-anchors (exactly one eligible group) and
        // existing demo / E2E create + review flows keep working unchanged.
        var seedGroupId = await dbContext.Groups
            .Where(g => g.Name == "Norte"
                && g.Process!.Status == ProcessStatus.Active
                && g.Process!.Fund!.Status == FundStatus.Active)
            .Select(g => g.Id)
            .FirstOrDefaultAsync();

        if (seedGroupId != 0)
        {
            foreach (var email in new[] { "applicant@programa-semilla.test", "reviewer@programa-semilla.test" })
            {
                var user = await userManager.FindByEmailAsync(email);
                if (user is null)
                    continue;
                if (!await dbContext.UserGroupMemberships.AnyAsync(m => m.UserId == user.Id && m.GroupId == seedGroupId))
                {
                    dbContext.UserGroupMemberships.Add(new UserGroupMembership(user.Id, seedGroupId));
                }
            }
            await dbContext.SaveChangesAsync();
        }

        // Spec 037 / T024 — seed two active companies for the demo applicant so the
        // application-create flow (and E2E) has selectable companies out of the box.
        // Two companies exercises the multi-company (explicit-choice) path by default.
        var demoApplicantUser = await userManager.FindByEmailAsync("applicant@programa-semilla.test");
        if (demoApplicantUser is not null)
        {
            var demoApplicantId = await dbContext.Applicants
                .Where(a => a.UserId == demoApplicantUser.Id)
                .Select(a => a.Id)
                .FirstOrDefaultAsync();
            if (demoApplicantId != 0
                && !await dbContext.Companies.AnyAsync(c => c.ApplicantId == demoApplicantId))
            {
                dbContext.Companies.Add(new Company(demoApplicantId, "Acme Consulting S.A."));
                dbContext.Companies.Add(new Company(demoApplicantId, "TechCorp Ltda."));
                await dbContext.SaveChangesAsync();
            }
        }
    }
}
