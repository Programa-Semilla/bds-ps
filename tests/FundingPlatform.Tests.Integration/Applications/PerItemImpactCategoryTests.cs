using FundingPlatform.Domain.Entities;
using FundingPlatform.Domain.Enums;
using FundingPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using AppEntity = FundingPlatform.Domain.Entities.Application;

namespace FundingPlatform.Tests.Integration.Applications;

/// <summary>
/// Spec 035 (evolved 2026-06-16) / TE019 — application-level impacts + per-item attribution
/// + category values persist and round-trip; a newly-added required category field blocks an
/// in-progress draft's submit but does NOT retroactively invalidate an already-submitted app.
/// </summary>
[TestFixture]
public class PerItemImpactCategoryTests
{
    private static AppDbContext CreateContext(string dbName) =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .ConfigureWarnings(w => w.Ignore(
                Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    private static async Task<(int appId, int categoryId, int templateId)> SeedAsync(
        string dbName, bool fillRequiredField)
    {
        using var ctx = CreateContext(dbName);

        var category = new Category("Equipo", null, isActive: true);
        category.AddField("modelo", "Modelo", ParameterDataType.Text, isRequired: true, sortOrder: 1);
        ctx.Categories.Add(category);

        var template = new ImpactTemplate("Empleo", null, isActive: true);
        template.AddParameter(new ImpactTemplateParameter("nuevos", "Nuevos empleos", ParameterDataType.Integer, true, null, 1));
        ctx.ImpactTemplates.Add(template);
        await ctx.SaveChangesAsync();

        var applicant = new Applicant("u1", "111", "Ana", "Mora", "ana@x.test", "88880000", null);
        ctx.Applicants.Add(applicant);
        await ctx.SaveChangesAsync();

        var app = new AppEntity(applicant.Id, 1, "ACME");
        app.AssignPublicCode(new FundingPlatform.Domain.ValueObjects.PublicCode("A7K2-9XF3"));
        // Spec 035 (evolved) — declare the impact at the application level (with values).
        var impact = app.AddImpact(template, new[] { new ImpactParameterValue(template.Parameters.First().Id, "5") });

        var modelField = category.Fields.First();
        var item = new Item("Laptop", category.Id);
        item.SetCategoryFieldValues(new[]
        {
            new CategoryFieldValue(modelField.Id, fillRequiredField ? "XPS 13" : null),
        });
        app.AddItem(item);
        ctx.Applications.Add(app);
        await ctx.SaveChangesAsync();

        // Attribute the item to the now-persisted declared impact + justification.
        item.AttributeImpacts(new[] { impact.Id });
        item.SetImpactJustification("Aporta empleo local");
        await ctx.SaveChangesAsync();

        return (app.Id, category.Id, template.Id);
    }

    private static IQueryable<AppEntity> WithDetails(AppDbContext ctx) =>
        ctx.Applications
            .Include(a => a.Items).ThenInclude(i => i.Category).ThenInclude(c => c.Fields)
            .Include(a => a.Items).ThenInclude(i => i.CategoryFieldValues).ThenInclude(v => v.CategoryField)
            .Include(a => a.Items).ThenInclude(i => i.ItemImpacts).ThenInclude(ii => ii.ApplicationImpact).ThenInclude(ai => ai.ImpactTemplate)
            .Include(a => a.Impacts).ThenInclude(ai => ai.ImpactTemplate)
            .Include(a => a.Impacts).ThenInclude(ai => ai.ParameterValues).ThenInclude(v => v.ImpactTemplateParameter);

    [Test]
    public async Task AppImpactAttributionAndCategoryValues_RoundTrip()
    {
        var dbName = $"pic-{Guid.NewGuid():N}";
        var (appId, _, _) = await SeedAsync(dbName, fillRequiredField: true);

        using var ctx = CreateContext(dbName);
        var app = await WithDetails(ctx).FirstAsync(a => a.Id == appId);
        var item = app.Items.Single();

        // App-level declared impact + its value.
        var declared = app.Impacts.Single();
        Assert.That(declared.ImpactTemplate!.Name, Is.EqualTo("Empleo"));
        Assert.That(declared.ParameterValues.Single().Value, Is.EqualTo("5"));

        // Per-item attribution targets the declared impact.
        Assert.That(item.ItemImpacts.Single().ApplicationImpactId, Is.EqualTo(declared.Id));
        Assert.That(item.ItemImpacts.Single().ApplicationImpact!.ImpactTemplate!.Name, Is.EqualTo("Empleo"));
        Assert.That(item.ImpactJustification, Is.EqualTo("Aporta empleo local"));

        // Category field round-trip.
        Assert.That(item.CategoryFieldValues.Single().Value, Is.EqualTo("XPS 13"));
        Assert.That(item.CategoryFieldValues.Single().CategoryField.DisplayLabel, Is.EqualTo("Modelo"));

        // Complete item → submit gate passes (min quotations 0 for this check).
        Assert.That(app.Validate(minQuotations: 0), Is.Empty);
    }

    [Test]
    public async Task RemoveImpact_StripsItemAttribution_RoundTrip()
    {
        var dbName = $"pic-{Guid.NewGuid():N}";
        var (appId, _, _) = await SeedAsync(dbName, fillRequiredField: true);

        using (var ctx = CreateContext(dbName))
        {
            var app = await WithDetails(ctx).FirstAsync(a => a.Id == appId);
            var declared = app.Impacts.Single();
            app.RemoveImpact(declared.Id);
            await ctx.SaveChangesAsync();
        }

        using (var ctx = CreateContext(dbName))
        {
            var app = await WithDetails(ctx).FirstAsync(a => a.Id == appId);
            Assert.That(app.Impacts, Is.Empty);
            Assert.That(app.Items.Single().ItemImpacts, Is.Empty,
                "SC-007 — removing a declared impact strips the line item's attribution.");
        }
    }

    [Test]
    public async Task NewlyAddedRequiredField_BlocksDraftSubmit_ButNotSubmittedApp()
    {
        var dbName = $"pic-{Guid.NewGuid():N}";
        var (appId, categoryId, _) = await SeedAsync(dbName, fillRequiredField: true);

        // Admin adds a NEW required field to the category after the item was saved.
        using (var ctx = CreateContext(dbName))
        {
            var category = await ctx.Categories.Include(c => c.Fields).FirstAsync(c => c.Id == categoryId);
            category.AddField("garantia", "Garantía", ParameterDataType.Text, isRequired: true, sortOrder: 2);
            await ctx.SaveChangesAsync();
        }

        // A Draft application's submit gate now reports the missing new field
        // (the item has no value row for it).
        using (var ctx = CreateContext(dbName))
        {
            var app = await WithDetails(ctx).FirstAsync(a => a.Id == appId);
            Assert.That(app.State, Is.EqualTo(ApplicationState.Draft));
            var errors = app.Validate(minQuotations: 0);
            Assert.That(errors, Has.Some.Contains("Garantía"),
                "A newly-added required field should block an in-progress draft's submit.");
        }

        // An already-submitted application is past Draft — Validate is not the gate
        // for it, so the new field does NOT retroactively invalidate it.
        using (var ctx = CreateContext(dbName))
        {
            var app = await WithDetails(ctx).FirstAsync(a => a.Id == appId);
            typeof(AppEntity).GetProperty("State")!.SetValue(app, ApplicationState.Submitted);
            await ctx.SaveChangesAsync();
        }

        using (var ctx = CreateContext(dbName))
        {
            var app = await ctx.Applications.FirstAsync(a => a.Id == appId);
            Assert.That(app.State, Is.EqualTo(ApplicationState.Submitted),
                "An already-submitted application keeps its state regardless of later category-field edits.");
        }
    }
}
