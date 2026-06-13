using FundingPlatform.Application.Admin.Users.Batch;
using FundingPlatform.Application.Admin.Users.DTOs;
using FundingPlatform.Application.Audit;
using FundingPlatform.Domain.Entities;
using FundingPlatform.Infrastructure.Audit;
using FundingPlatform.Infrastructure.Identity;
using FundingPlatform.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FundingPlatform.Tests.Integration.Application;

/// <summary>
/// Spec 034 — DB-backed (EF InMemory, real-DB-shaped) coverage for the CSV bulk
/// applicant provisioning service (<c>CreateUsersBatchAsync</c>). Mirrors the
/// <see cref="UserAdministrationUserCodeTests"/> setup. CSV parsing + the
/// invitation send are out of scope here (controller-level); this exercises the
/// per-row validate → resolve chain → create → report pipeline.
/// </summary>
[TestFixture]
public class BatchUserCreationTests
{
    private const string ActorAdminId = "actor-admin-1";

    private static (UserAdministrationService service, AppDbContext ctx, IServiceProvider sp) Build()
    {
        var dbName = $"batch-users-{Guid.NewGuid():N}";
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<AppDbContext>(o => o
            .UseInMemoryDatabase(dbName)
            .ConfigureWarnings(w => w.Ignore(
                Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning)));
        services.AddIdentity<ApplicationUser, IdentityRole>(o =>
            {
                o.Password.RequireDigit = false;
                o.Password.RequireLowercase = false;
                o.Password.RequireUppercase = false;
                o.Password.RequireNonAlphanumeric = false;
                o.Password.RequiredLength = 4;
            })
            .AddEntityFrameworkStores<AppDbContext>();
        services.AddScoped<IAdminAuditWriter, AdminAuditWriter>();
        services.AddScoped<UserAdministrationService>();
        services.AddHttpContextAccessor();

        var sp = services.BuildServiceProvider();
        var ctx = sp.GetRequiredService<AppDbContext>();
        var sut = sp.GetRequiredService<UserAdministrationService>();
        return (sut, ctx, sp);
    }

    private static async Task SeedRolesAsync(IServiceProvider sp)
    {
        var roleMgr = sp.GetRequiredService<RoleManager<IdentityRole>>();
        foreach (var r in new[] { "Applicant", "Reviewer", "SupplierAdmin", "Admin" })
        {
            if (!await roleMgr.RoleExistsAsync(r))
            {
                await roleMgr.CreateAsync(new IdentityRole(r));
            }
        }
    }

    /// <summary>Seeds a Fund → Process → Groups chain and returns it for assertions.</summary>
    private static async Task<(Fund fund, Process process)> SeedChainAsync(
        AppDbContext ctx, string fundName, string processName, params string[] groupNames)
    {
        var fund = Fund.Create(fundName, $"{fundName} desc");
        ctx.Funds.Add(fund);
        await ctx.SaveChangesAsync();

        var process = Process.Create(processName, fund.Id);
        ctx.Processes.Add(process);
        await ctx.SaveChangesAsync();

        foreach (var n in groupNames)
        {
            ctx.Groups.Add(Group.Create(n, process.Id));
        }
        await ctx.SaveChangesAsync();
        return (fund, process);
    }

    private static BatchUserImportRow Row(
        int n, string grupo, string proceso, string fondo,
        string nombre, string ap1, string ap2,
        string email, string telefono, string cedula, string codigo) =>
        new(n, grupo, proceso, fondo, nombre, ap1, ap2, email, telefono, cedula, codigo);

    // ---- US1 ------------------------------------------------------------------

    [Test]
    public async Task AllValid_CreatesInvitedApplicantsWithGroupAndCode()
    {
        var (sut, ctx, sp) = Build();
        await SeedRolesAsync(sp);
        await SeedChainAsync(ctx, "Fondo General", "Migración inicial", "Norte", "Sur");
        var norte = await ctx.Groups.SingleAsync(g => g.Name == "Norte");
        var sur = await ctx.Groups.SingleAsync(g => g.Name == "Sur");

        var rows = new[]
        {
            Row(1, "Norte", "Migración inicial", "Fondo General", "Ana", "Rojas", "Mora",
                "ana.rojas@example.cr", "506 8888 1111", "1-1234-5678", "COD-001"),
            Row(2, "Sur", "Migración inicial", "Fondo General", "Luis", "Mora", "",
                "luis.mora@example.cr", "7777-2222 / 8888-3333", "2-3456-7890", "COD-002"),
        };

        var result = await sut.CreateUsersBatchAsync(rows, ActorAdminId, CancellationToken.None);

        Assert.That(result.Succeeded.Count, Is.EqualTo(2));
        Assert.That(result.Errored, Is.Empty);

        var ana = await ctx.Applicants.SingleAsync(a => a.Email == "ana.rojas@example.cr");
        Assert.That(ana.LegalId, Is.EqualTo("1-1234-5678"));
        Assert.That(ana.UserCode, Is.EqualTo("COD-001"));
        Assert.That(ana.LastName, Is.EqualTo("Rojas Mora"));
        Assert.That(ana.Phone, Is.EqualTo("88881111")); // 506 prefix stripped

        var luis = await ctx.Applicants.SingleAsync(a => a.Email == "luis.mora@example.cr");
        Assert.That(luis.LastName, Is.EqualTo("Mora")); // no second surname
        Assert.That(luis.Phone, Is.EqualTo("77772222")); // first of two numbers

        var memberships = await ctx.UserGroupMemberships.ToListAsync();
        Assert.That(memberships.Count, Is.EqualTo(2));
        Assert.That(memberships.Count(m => m.GroupId == norte.Id), Is.EqualTo(1));
        Assert.That(memberships.Count(m => m.GroupId == sur.Id), Is.EqualTo(1));
    }

    [Test]
    public async Task InfersIdentificationType_AndTakesFirstEmail()
    {
        var (sut, ctx, sp) = Build();
        await SeedRolesAsync(sp);
        await SeedChainAsync(ctx, "Fondo General", "Migración inicial", "Norte");
        var c = ("Norte", "Migración inicial", "Fondo General");

        var rows = new[]
        {
            // 9-digit value → cédula física
            Row(1, c.Item1, c.Item2, c.Item3, "Ana", "Rojas", "", "ana@example.cr", "", "1-1774-0469", "COD-1"),
            // 12-digit value (real DIMEX from the client file) → DIMEX
            Row(2, c.Item1, c.Item2, c.Item3, "Geo", "Flores", "", "geo@example.cr", "", "155822492214", "COD-2"),
            // multiple emails → first is used as the account email
            Row(3, c.Item1, c.Item2, c.Item3, "Gia", "Maddaloni", "",
                "giancarlomaddaloni@gmail.com / giancarlo@theglutenfreelab.com", "", "1-1542-0896", "COD-3"),
        };

        var result = await sut.CreateUsersBatchAsync(rows, ActorAdminId, CancellationToken.None);

        Assert.That(result.Succeeded.Count, Is.EqualTo(3), string.Join(" | ", result.Errored.Select(e => $"{e.RowNumber}:{e.Reason}")));

        var fisica = await ctx.Applicants.SingleAsync(a => a.Email == "ana@example.cr");
        Assert.That(fisica.IdentificationType, Is.EqualTo(Domain.Enums.IdentificationType.CedulaFisica));

        var dimex = await ctx.Applicants.SingleAsync(a => a.Email == "geo@example.cr");
        Assert.That(dimex.IdentificationType, Is.EqualTo(Domain.Enums.IdentificationType.Dimex));
        Assert.That(dimex.LegalId, Is.EqualTo("155822492214"));

        // The account email is the FIRST of the two listed addresses.
        var gia = await ctx.Applicants.SingleAsync(a => a.Email == "giancarlomaddaloni@gmail.com");
        Assert.That(gia, Is.Not.Null);
    }

    // ---- US2 ------------------------------------------------------------------

    [Test]
    public async Task Mixed_CreatesValid_SkipsInvalid_WithReasons()
    {
        var (sut, ctx, sp) = Build();
        await SeedRolesAsync(sp);
        await SeedChainAsync(ctx, "Fondo General", "Migración inicial", "Norte");

        // Pre-existing DB user to trip the "ya está en uso" path.
        var pre = await sut.CreateUserAsync(
            new CreateUserRequest("Pre", "Existente", "existing@example.cr", null, "Applicant",
                "9-8765-4321", GroupIds: new[] { (await ctx.Groups.SingleAsync(g => g.Name == "Norte")).Id },
                IdentificationType: Domain.Enums.IdentificationType.CedulaFisica, UserCode: "PRE-001"),
            ActorAdminId, CancellationToken.None);
        Assert.That(pre.Succeeded, Is.True);

        var c = ("Norte", "Migración inicial", "Fondo General");
        var rows = new[]
        {
            // 1: valid
            Row(1, c.Item1, c.Item2, c.Item3, "Ana", "Rojas", "", "ana@example.cr", "", "1-1234-5678", "COD-1"),
            // 2: blank email
            Row(2, c.Item1, c.Item2, c.Item3, "Bob", "Soto", "", "", "", "1-2222-3333", "COD-2"),
            // 3: unrecognized id shape (10 digits → not an individual id → errored)
            Row(3, c.Item1, c.Item2, c.Item3, "Cyn", "Vega", "", "cyn@example.cr", "", "1234567890", "COD-3"),
            // 4: oversized código (51 chars)
            Row(4, c.Item1, c.Item2, c.Item3, "Dan", "Lara", "", "dan@example.cr", "", "1-4444-5555", new string('X', 51)),
            // 5: duplicate código in-file (COD-1 again)
            Row(5, c.Item1, c.Item2, c.Item3, "Eva", "Pino", "", "eva@example.cr", "", "1-6666-7777", "COD-1"),
            // 6: email already in system
            Row(6, c.Item1, c.Item2, c.Item3, "Fred", "Mena", "", "existing@example.cr", "", "1-8888-9999", "COD-6"),
        };

        var result = await sut.CreateUsersBatchAsync(rows, ActorAdminId, CancellationToken.None);

        Assert.That(result.Succeeded.Count + result.Errored.Count, Is.EqualTo(rows.Length));
        Assert.That(result.Succeeded.Select(o => o.RowNumber), Is.EquivalentTo(new[] { 1 }));

        string Reason(int n) => result.Errored.Single(o => o.RowNumber == n).Reason!;
        Assert.That(Reason(2), Is.EqualTo(BatchUserRowReasons.EmailBlank));
        Assert.That(Reason(3), Is.EqualTo(BatchUserRowReasons.CedulaInvalid));
        Assert.That(Reason(4), Is.EqualTo(BatchUserRowReasons.CodigoTooLong));
        Assert.That(Reason(5), Is.EqualTo(BatchUserRowReasons.CodigoDupInFile));
        Assert.That(Reason(6), Is.EqualTo(BatchUserRowReasons.EmailInUse));

        // Only the one valid row created an account (plus the pre-existing seed).
        var created = await ctx.Applicants.CountAsync();
        Assert.That(created, Is.EqualTo(2));
    }

    // ---- US3 ------------------------------------------------------------------

    [Test]
    public async Task WrongChain_RowSkipped()
    {
        var (sut, ctx, sp) = Build();
        await SeedRolesAsync(sp);
        // Chain A: "Norte" sits under Process "Migración inicial" / Fund "Fondo General".
        await SeedChainAsync(ctx, "Fondo General", "Migración inicial", "Norte");
        // Chain B: a different Fund/Process with its own group.
        await SeedChainAsync(ctx, "Fondo Especial", "Convocatoria 2025", "Este");

        var rows = new[]
        {
            // Coherent — Norte really is under Migración inicial / Fondo General.
            Row(1, "Norte", "Migración inicial", "Fondo General", "Ana", "Rojas", "",
                "ana@example.cr", "", "1-1234-5678", "COD-1"),
            // Wrong chain — Norte named with the OTHER process/fund.
            Row(2, "Norte", "Convocatoria 2025", "Fondo Especial", "Bob", "Soto", "",
                "bob@example.cr", "", "1-2222-3333", "COD-2"),
        };

        var result = await sut.CreateUsersBatchAsync(rows, ActorAdminId, CancellationToken.None);

        Assert.That(result.Succeeded.Select(o => o.RowNumber), Is.EquivalentTo(new[] { 1 }));
        var bad = result.Errored.Single(o => o.RowNumber == 2);
        Assert.That(bad.Reason, Is.EqualTo(BatchUserRowReasons.ChainMismatch));
    }

    [Test]
    public async Task UnknownNames_AreReportedDistinctly()
    {
        var (sut, ctx, sp) = Build();
        await SeedRolesAsync(sp);
        await SeedChainAsync(ctx, "Fondo General", "Migración inicial", "Norte");

        var rows = new[]
        {
            Row(1, "Inexistente", "Migración inicial", "Fondo General", "A", "B", "", "a@example.cr", "", "1-1234-5678", "C1"),
            Row(2, "Norte", "Proceso X", "Fondo General", "A", "B", "", "b@example.cr", "", "1-2222-3333", "C2"),
            Row(3, "Norte", "Migración inicial", "Fondo X", "A", "B", "", "c@example.cr", "", "1-4444-5555", "C3"),
        };

        var result = await sut.CreateUsersBatchAsync(rows, ActorAdminId, CancellationToken.None);

        Assert.That(result.Succeeded, Is.Empty);
        Assert.That(result.Errored.Single(o => o.RowNumber == 1).Reason, Is.EqualTo(BatchUserRowReasons.GroupNotFound));
        Assert.That(result.Errored.Single(o => o.RowNumber == 2).Reason, Is.EqualTo(BatchUserRowReasons.ProcessNotFound));
        Assert.That(result.Errored.Single(o => o.RowNumber == 3).Reason, Is.EqualTo(BatchUserRowReasons.FundNotFound));
    }

    // ---- Template contract (T031) --------------------------------------------

    [Test]
    public void Template_Header_MatchesCanonicalColumns()
    {
        Assert.That(BatchUserCsvColumns.Ordered, Is.EqualTo(new[]
        {
            "Grupo", "Proceso", "Fondo", "Nombre", "Apellido 1", "Apellido 2",
            "Email", "Teléfono", "Cédula", "Código de usuario",
        }));
        // Accent/case-insensitive, BOM-tolerant header match (FR-003).
        Assert.That(BatchUserCsvColumns.HeaderMatches(new[]
        {
            "﻿grupo", "PROCESO", "fondo", "nombre", "apellido 1", "apellido 2",
            "email", "telefono", "cedula", "codigo de usuario",
        }), Is.True);
    }
}
