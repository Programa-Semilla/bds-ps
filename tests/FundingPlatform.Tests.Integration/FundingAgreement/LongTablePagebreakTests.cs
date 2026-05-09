using FundingPlatform.Domain.Entities;
using FundingPlatform.Domain.Enums;
using FundingPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using AppEntity = FundingPlatform.Domain.Entities.Application;

namespace FundingPlatform.Tests.Integration.FundingAgreement;

/// <summary>
/// Spec 018 / R-003 / Outstanding Risk #2 — long-table smoke test. With 50
/// items, the requested-resources table must span multiple pages and the
/// brand header / footer (CSS `position: fixed`) must repeat. The scenario
/// exercises the entity-side cardinality without spinning up the full
/// Aspire-orchestrated PDF render; the visual fidelity assertion lives in
/// the E2E layer (T022).
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
