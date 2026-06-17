using FundingPlatform.Application.Admin.Commands;
using FundingPlatform.Application.Services;
using FundingPlatform.Infrastructure.Persistence;
using FundingPlatform.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace FundingPlatform.Tests.Integration.Admin;

/// <summary>
/// Spec 035 / US1 / T031 — admin category-field configuration round-trips through
/// the EF model (create with fields, full-replace update, deactivate,
/// GetByIdWithFields).
/// </summary>
[TestFixture]
public class CategoryAdministrationTests
{
    private static AppDbContext CreateContext(string dbName) =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .ConfigureWarnings(w => w.Ignore(
                Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    private static AdminService MakeService(AppDbContext ctx) =>
        new(new ImpactTemplateRepository(ctx),
            new SystemConfigurationRepository(ctx),
            new CategoryRepository(ctx));

    [Test]
    public async Task CreateCategory_WithFields_PersistsAndRoundTrips()
    {
        var dbName = $"cat-{Guid.NewGuid():N}";
        int id;
        using (var ctx = CreateContext(dbName))
        {
            var svc = MakeService(ctx);
            id = await svc.CreateCategoryAsync(new CreateCategoryCommand(
                "Equipo", "Bienes de equipo", new List<CategoryFieldDefinition>
                {
                    new("marca", "Marca", "Text", false, 1),
                    new("modelo", "Modelo", "Text", true, 2),
                    new("costo", "Costo unitario", "Decimal", true, 3),
                }));
        }

        using (var ctx = CreateContext(dbName))
        {
            var svc = MakeService(ctx);
            var detail = await svc.GetCategoryByIdAsync(id);
            Assert.That(detail, Is.Not.Null);
            Assert.That(detail!.Name, Is.EqualTo("Equipo"));
            Assert.That(detail.IsActive, Is.True);
            Assert.That(detail.Fields, Has.Count.EqualTo(3));
            Assert.That(detail.Fields[0].DisplayLabel, Is.EqualTo("Marca"));
            Assert.That(detail.Fields[1].IsRequired, Is.True);
            Assert.That(detail.Fields[2].DataType, Is.EqualTo("Decimal"));
        }
    }

    [Test]
    public async Task UpdateCategory_FullReplacesFieldsAndDeactivates()
    {
        var dbName = $"cat-{Guid.NewGuid():N}";
        int id;
        using (var ctx = CreateContext(dbName))
        {
            var svc = MakeService(ctx);
            id = await svc.CreateCategoryAsync(new CreateCategoryCommand(
                "Equipo", null, new List<CategoryFieldDefinition>
                {
                    new("marca", "Marca", "Text", false, 1),
                    new("modelo", "Modelo", "Text", true, 2),
                }));
        }

        using (var ctx = CreateContext(dbName))
        {
            var svc = MakeService(ctx);
            await svc.UpdateCategoryAsync(new UpdateCategoryCommand(
                id, "Equipo de cómputo", "Renombrada", IsActive: false,
                new List<CategoryFieldDefinition>
                {
                    new("serie", "Número de serie", "Text", true, 1),
                }));
        }

        using (var ctx = CreateContext(dbName))
        {
            var svc = MakeService(ctx);
            var detail = await svc.GetCategoryByIdAsync(id);
            Assert.That(detail!.Name, Is.EqualTo("Equipo de cómputo"));
            Assert.That(detail.IsActive, Is.False);
            Assert.That(detail.Fields, Has.Count.EqualTo(1));
            Assert.That(detail.Fields[0].DisplayLabel, Is.EqualTo("Número de serie"));
        }
    }

    [Test]
    public async Task GetAllCategories_ReportsFieldCounts()
    {
        var dbName = $"cat-{Guid.NewGuid():N}";
        using (var ctx = CreateContext(dbName))
        {
            var svc = MakeService(ctx);
            await svc.CreateCategoryAsync(new CreateCategoryCommand(
                "ConCampos", null, new List<CategoryFieldDefinition>
                {
                    new("a", "A", "Text", false, 1),
                    new("b", "B", "Text", false, 2),
                }));
            await svc.CreateCategoryAsync(new CreateCategoryCommand(
                "SinCampos", null, new List<CategoryFieldDefinition>()));
        }

        using (var ctx = CreateContext(dbName))
        {
            var svc = MakeService(ctx);
            var all = await svc.GetAllCategoriesAsync();
            Assert.That(all.Single(c => c.Name == "ConCampos").FieldCount, Is.EqualTo(2));
            Assert.That(all.Single(c => c.Name == "SinCampos").FieldCount, Is.EqualTo(0));
        }
    }
}
