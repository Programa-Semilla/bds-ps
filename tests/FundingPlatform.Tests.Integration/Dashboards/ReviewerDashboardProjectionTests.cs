using FundingPlatform.Application.Abstractions;
using FundingPlatform.Domain.Entities;
using FundingPlatform.Domain.Enums;
using FundingPlatform.Domain.ValueObjects;
using FundingPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using AppEntity = FundingPlatform.Domain.Entities.Application;

namespace FundingPlatform.Tests.Integration.Dashboards;

/// <summary>
/// Spec 021 / US6 / FR-033 (evolution 2026-05-25) — the reviewer-dashboard
/// "pending" tile must count distinct <c>Submitted</c> applications awaiting
/// review, NOT individual quotations. An application with two competing quotes
/// on one item is one unit of pending work, not two.
/// </summary>
[TestFixture]
public class ReviewerDashboardProjectionTests
{
    private static AppDbContext CreateContext(string dbName) =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .ConfigureWarnings(w => w.Ignore(
                Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    [Test]
    public async Task CountPendingApplications_CountsSubmittedApps_NotIndividualQuotations()
    {
        var dbName = $"rev-dash-{Guid.NewGuid():N}";

        using (var ctx = CreateContext(dbName))
        {
            // Spec 029 — anchor applications to an Active Fund→Process→Group chain.
            var fund = Fund.Create("Fondo de prueba", "Fondo de prueba para tests.");
            ctx.Funds.Add(fund);
            await ctx.SaveChangesAsync();
            var process = Process.Create("Proceso de prueba", fund.Id);
            ctx.Processes.Add(process);
            await ctx.SaveChangesAsync();
            var group = Group.Create("Grupo de prueba", process.Id);
            ctx.Groups.Add(group);
            await ctx.SaveChangesAsync();

            var applicant = new Applicant(
                userId: $"user-{Guid.NewGuid():N}",
                legalId: "REVD-1",
                firstName: "Rev", lastName: "Dash",
                email: "revd@example.com", phone: null, performanceScore: null);
            ctx.Applicants.Add(applicant);
            await ctx.SaveChangesAsync();

            var category = new Category("Equipment", "desc", isActive: true);
            ctx.Categories.Add(category);
            await ctx.SaveChangesAsync();

            // One SUBMITTED application: one item, TWO competing quotations.
            var submitted = new AppEntity(applicant.Id, group.Id, "Submitted Co");
            submitted.AssignPublicCode(FundingPlatform.Tests.Integration.Helpers.TestPublicCodes.Next());
            submitted.AddItem(new Item("Widget", category.Id));
            typeof(AppEntity).GetProperty("State")!.SetValue(submitted, ApplicationState.Submitted);
            ctx.Applications.Add(submitted);

            // One DRAFT application — must be excluded from the pending count.
            var draft = new AppEntity(applicant.Id, group.Id, "Draft Co");
            draft.AssignPublicCode(FundingPlatform.Tests.Integration.Helpers.TestPublicCodes.Next());
            draft.AddItem(new Item("Gadget", category.Id));
            ctx.Applications.Add(draft);
            await ctx.SaveChangesAsync();

            var suppliers = Enumerable.Range(1, 2).Select(i =>
                Supplier.CreateDraft(
                    legalId: $"REVD-S{i}", name: $"Supplier {i}",
                    createdByApplicantId: applicant.Id,
                    firstBranchName: "Sede", firstBranchContactName: null,
                    firstBranchEmail: null, firstBranchPhone: null, firstBranchAddressLine: null,
                    firstBranchProvince: "San Jose", firstBranchShippingDetails: null,
                    firstBranchWarrantyInfo: null)).ToList();
            ctx.Suppliers.AddRange(suppliers);
            await ctx.SaveChangesAsync();

            var docs = Enumerable.Range(1, 2)
                .Select(i => new Document($"q{i}.pdf", $"k{i}", 1L, "application/pdf")).ToList();
            ctx.Documents.AddRange(docs);
            await ctx.SaveChangesAsync();

            var validUntil = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(1));
            submitted.Items[0].AddQuotation(suppliers[0], suppliers[0].Branches.First(), docs[0], 100_000m, validUntil, "CRC");
            submitted.Items[0].AddQuotation(suppliers[1], suppliers[1].Branches.First(), docs[1], 120_000m, validUntil, "CRC");
            await ctx.SaveChangesAsync();
        }

        using (var ctx = CreateContext(dbName))
        {
            IApplicationQueryFilter filter = new ApplicationQueryFilter();
            var projection = new ReviewerDashboardProjection(ctx, filter);

            var count = await projection.CountPendingApplicationsAsync(CancellationToken.None);

            Assert.That(count, Is.EqualTo(1),
                "Pending tile must count the one Submitted application, not its two competing quotations.");
        }
    }
}
