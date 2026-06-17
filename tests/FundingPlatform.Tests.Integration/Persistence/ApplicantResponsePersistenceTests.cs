using FundingPlatform.Domain.Entities;
using FundingPlatform.Domain.Enums;
using FundingPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using AppEntity = FundingPlatform.Domain.Entities.Application;

namespace FundingPlatform.Tests.Integration.Persistence;

[TestFixture]
public class ApplicantResponsePersistenceTests
{
    private AppDbContext CreateContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        return new AppDbContext(options);
    }

    [Test]
    public async Task ApplicantResponse_WithItemResponses_RoundTripsAndCascadeDeletes()
    {
        var dbName = $"arp-{Guid.NewGuid():N}";

        int applicationId;
        int responseId;

        using (var ctx = CreateContext(dbName))
        {
            var application = SeedApplicationWithItems(ctx, new[] { "Item A", "Item B" });
            await ctx.SaveChangesAsync();
            applicationId = application.Id;

            var itemIds = application.Items.Select(i => i.Id).ToList();
            var response = application.SubmitResponse(
                new Dictionary<int, ItemResponseDecision>
                {
                    [itemIds[0]] = ItemResponseDecision.Accept,
                    [itemIds[1]] = ItemResponseDecision.Reject
                },
                "user-1");

            // Bypass state-machine: the InMemory context doesn't need a Resolved precondition
            // because we manipulated SubmitResponse above which requires Resolved; set it explicitly.
            // We set state before submitting — re-do here for clarity:
            await ctx.SaveChangesAsync();
            responseId = response.Id;
        }

        using (var ctx = CreateContext(dbName))
        {
            var loaded = await ctx.ApplicantResponses
                .Include(r => r.ItemResponses)
                .FirstAsync(r => r.Id == responseId);

            Assert.That(loaded.ItemResponses, Has.Count.EqualTo(2));
            Assert.That(loaded.ItemResponses.Any(ir => ir.Decision == ItemResponseDecision.Accept), Is.True);
            Assert.That(loaded.ItemResponses.Any(ir => ir.Decision == ItemResponseDecision.Reject), Is.True);
        }

        using (var ctx = CreateContext(dbName))
        {
            var response = await ctx.ApplicantResponses
                .Include(r => r.ItemResponses)
                .FirstAsync(r => r.Id == responseId);
            ctx.ApplicantResponses.Remove(response);
            await ctx.SaveChangesAsync();

            var itemResponseCount = await ctx.Set<ItemResponse>().CountAsync(ir => ir.ApplicantResponseId == responseId);
            Assert.That(itemResponseCount, Is.EqualTo(0), "Child ItemResponses should cascade-delete.");
        }
    }

    [Test]
    public async Task Appeal_WithMessages_RoundTripsAndCascadeDeletes()
    {
        var dbName = $"app-{Guid.NewGuid():N}";

        int appealId;

        using (var ctx = CreateContext(dbName))
        {
            var application = SeedApplicationWithItems(ctx, new[] { "Item A" });
            await ctx.SaveChangesAsync();

            var itemIds = application.Items.Select(i => i.Id).ToList();
            application.SubmitResponse(
                new Dictionary<int, ItemResponseDecision> { [itemIds[0]] = ItemResponseDecision.Reject },
                "applicant-1");
            await ctx.SaveChangesAsync();

            var appeal = application.OpenAppeal("applicant-1", maxAppeals: 1);
            appeal.PostMessage("applicant-1", "Please reconsider.");
            appeal.PostMessage("reviewer-1", "Here is our context.");
            await ctx.SaveChangesAsync();
            appealId = appeal.Id;
        }

        using (var ctx = CreateContext(dbName))
        {
            var loaded = await ctx.Appeals
                .Include(a => a.Messages)
                .FirstAsync(a => a.Id == appealId);

            Assert.That(loaded.Status, Is.EqualTo(AppealStatus.Open));
            Assert.That(loaded.Messages, Has.Count.EqualTo(2));
        }

        using (var ctx = CreateContext(dbName))
        {
            var appeal = await ctx.Appeals
                .Include(a => a.Messages)
                .FirstAsync(a => a.Id == appealId);
            ctx.Appeals.Remove(appeal);
            await ctx.SaveChangesAsync();

            var msgCount = await ctx.Set<AppealMessage>().CountAsync(m => m.AppealId == appealId);
            Assert.That(msgCount, Is.EqualTo(0), "Child AppealMessages should cascade-delete.");
        }
    }

    private static AppEntity SeedApplicationWithItems(AppDbContext ctx, string[] itemNames)
    {
        var applicant = new Applicant(
            userId: $"user-{Guid.NewGuid():N}",
            legalId: "LEG-1",
            firstName: "Test",
            lastName: "Applicant",
            email: "test@example.com",
            phone: null,
            performanceScore: null);
        ctx.Applicants.Add(applicant);
        ctx.SaveChanges();

        var category = new Category("Test Category", "desc", isActive: true);
        ctx.Categories.Add(category);
        ctx.SaveChanges();

        var application = new AppEntity(applicant.Id, 1, null,"Test Company");
        application.AssignPublicCode(FundingPlatform.Tests.Integration.Helpers.TestPublicCodes.Next());
        foreach (var name in itemNames)
        {
            application.AddItem(new Item(name, category.Id));
        }

        // Simulate prior state machine transitions up to Resolved
        typeof(AppEntity).GetProperty("State")!.SetValue(application, ApplicationState.Resolved);

        ctx.Applications.Add(application);
        return application;
    }
}
