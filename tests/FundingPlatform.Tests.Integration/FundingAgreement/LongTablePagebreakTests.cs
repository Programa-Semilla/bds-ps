using FundingPlatform.Domain.Entities;
using FundingPlatform.Domain.Enums;
using FundingPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using AppEntity = FundingPlatform.Domain.Entities.Application;

namespace FundingPlatform.Tests.Integration.FundingAgreement;

/// <summary>
/// Spec 018 / R-003 / Outstanding Risk #2 — entity-side cardinality smoke for the
/// long-table scenario.
///
/// <para>
/// **Scope:** This integration test only exercises the aggregate-root path
/// (<see cref="FundingPlatform.Domain.Entities.Application.AssignLineCodeToItem"/>)
/// 50 times to confirm the per-Application uniqueness invariant scales without
/// rejecting any of the distinct codes. It does NOT render a PDF, does NOT run
/// <c>pdftotext -layout</c>, and does NOT verify that the brand header / footer
/// CSS (<c>position: fixed</c>) repeats across page breaks. Those assertions
/// require the live Aspire-orchestrated PDF render path and live in the E2E
/// layer (T022 — <c>FundingAgreementPdfDownloadTests</c>) where the renderer is
/// available.
/// </para>
///
/// <para>
/// **Why it lives here:** the entity-level invariant cost (50 calls to
/// <see cref="FundingPlatform.Domain.Entities.Application.AssignLineCodeToItem"/>)
/// is cheap to assert without spinning up AppHost, and a regression in the
/// uniqueness loop would surface here before the slower E2E pipeline catches it.
/// </para>
/// </summary>
[TestFixture]
public class LongTablePagebreakTests
{
    private static AppDbContext NewContext(string name)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(name)
            .ConfigureWarnings(w => w.Ignore(
                Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new AppDbContext(options);
    }

    [Test]
    public async Task FiftyItems_AllReceiveLineCodesAndProjectInOrder()
    {
        var dbName = $"long-table-{Guid.NewGuid():N}";
        using var ctx = NewContext(dbName);

        var applicant = new Applicant(
            userId: $"u-{Guid.NewGuid():N}",
            legalId: "1-1234-5678",
            firstName: "Test",
            lastName: "Applicant",
            email: $"a-{Guid.NewGuid():N}@example.com",
            phone: null,
            performanceScore: null);
        ctx.Applicants.Add(applicant);

        var category = new Category("Equipment", "desc", isActive: true);
        ctx.Categories.Add(category);
        await ctx.SaveChangesAsync();

        var app = new AppEntity(applicant.Id, "Long-Table Test Co.");
        app.AssignPublicCode(FundingPlatform.Tests.Integration.Helpers.TestPublicCodes.Next());
        for (var i = 0; i < 50; i++)
        {
            app.AddItem(new Item($"Producto {i + 1:D2}", category.Id, "specs"));
        }
        ctx.Applications.Add(app);
        await ctx.SaveChangesAsync();

        // Apply distinct line codes — exercises the per-Application uniqueness
        // path 50 times. If this passes, the projection has 50 rows feeding the
        // requested-resources table, which forces multi-page layout in the PDF
        // renderer.
        var i2 = 0;
        foreach (var item in app.Items.ToList())
        {
            i2++;
            app.AssignLineCodeToItem(item.Id, $"T1-{i2:D2}");
        }
        typeof(AppEntity).GetProperty("State")!.SetValue(app, ApplicationState.UnderReview);
        await ctx.SaveChangesAsync();

        Assert.That(app.Items, Has.Count.EqualTo(50));
        Assert.That(app.Items.Select(i => i.LineCode).Distinct().Count(), Is.EqualTo(50));
        Assert.That(app.Items.All(i => !string.IsNullOrEmpty(i.LineCode)), Is.True);
    }
}
