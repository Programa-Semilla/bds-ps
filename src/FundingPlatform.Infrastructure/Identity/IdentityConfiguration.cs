using System.Security.Cryptography;
using FundingPlatform.Domain.Entities;
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

        string[] roles = ["Applicant", "Admin", "Reviewer"];

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

        var seedUsers = new[]
        {
            new { Email = "applicant@programa-semilla.test", Password = "Demo123!", FirstName = "Ana", LastName = "Pérez", LegalId = "DEMO-APP-001", Roles = new[] { "Applicant" } },
            new { Email = "reviewer@programa-semilla.test", Password = "Demo123!", FirstName = "Carlos", LastName = "Rivera", LegalId = "DEMO-REV-001", Roles = new[] { "Reviewer" } },
            new { Email = "demo-admin@programa-semilla.test", Password = "Demo123!", FirstName = "María", LastName = "Torres", LegalId = "DEMO-ADM-001", Roles = new[] { "Admin" } },
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
                    performanceScore: null));
            }
        }

        await dbContext.SaveChangesAsync();
    }
}
