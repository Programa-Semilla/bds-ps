using FundingPlatform.Domain.Entities;
using FundingPlatform.Domain.Enums;
using FundingPlatform.Domain.Interfaces;
using FundingPlatform.Infrastructure.Persistence;
using FundingPlatform.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace FundingPlatform.Tests.Integration.Persistence;

/// <summary>
/// EF in-memory tests for the spec 013 additions to ISupplierRepository.
/// The filtered unique index UX_SupplierBranches_DefaultPerSupplier cannot be
/// exercised against the in-memory provider — that invariant is covered by the
/// domain-level test in SupplierTests.AddBranch_RejectsSecondDefault and by the
/// SQL-level CREATE UNIQUE INDEX in dbo.SupplierBranches.sql.
/// </summary>
[TestFixture]
public class SupplierRepositoryTests
{
    private AppDbContext CreateContext(string dbName) =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .ConfigureWarnings(w => w.Ignore(
                Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    [Test]
    public async Task GetByLegalIdWithBranchesAsync_ReturnsSupplierAndBranches()
    {
        var dbName = $"sup-getlegal-{Guid.NewGuid():N}";

        using (var ctx = CreateContext(dbName))
        {
            var supplier = Supplier.CreateDraft("3-101-100001", "ACME", 1, "Sede principal",
                "Ana", "ana@x.com", "8888-8888", "200m sur", "San José", null, null);
            supplier.AddBranch("Sucursal Norte", "Pedro", "p@x.com", null, null, "Heredia", null, null, 1);
            ctx.Suppliers.Add(supplier);
            await ctx.SaveChangesAsync();
        }

        using (var ctx = CreateContext(dbName))
        {
            var repo = new SupplierRepository(ctx);
            var result = await repo.GetByLegalIdWithBranchesAsync("3-101-100001");

            Assert.That(result, Is.Not.Null);
            Assert.That(result!.Branches.Count, Is.EqualTo(2));
            Assert.That(result.Branches.Any(b => b.IsDefault), Is.True);
            Assert.That(result.Branches.Any(b => b.BranchName == "Sucursal Norte"), Is.True);
        }
    }

    [Test]
    public async Task GetByLegalIdWithBranchesAsync_IsCaseInsensitiveAndTrimsInput()
    {
        var dbName = $"sup-norm-{Guid.NewGuid():N}";

        using (var ctx = CreateContext(dbName))
        {
            ctx.Suppliers.Add(Supplier.CreateDraft("3-101-200002", "Beta", 1, "Sede principal",
                null, null, null, null, null, null, null));
            await ctx.SaveChangesAsync();
        }

        using (var ctx = CreateContext(dbName))
        {
            var repo = new SupplierRepository(ctx);
            var byLower = await repo.GetByLegalIdWithBranchesAsync("3-101-200002");
            var byUpperPadded = await repo.GetByLegalIdWithBranchesAsync("  3-101-200002  ");

            Assert.That(byLower, Is.Not.Null);
            Assert.That(byUpperPadded, Is.Not.Null);
            Assert.That(byLower!.Id, Is.EqualTo(byUpperPadded!.Id));
        }
    }

    [Test]
    public async Task ListForAdminAsync_FiltersByStatus()
    {
        var dbName = $"sup-admin-status-{Guid.NewGuid():N}";

        using (var ctx = CreateContext(dbName))
        {
            ctx.Suppliers.AddRange(MakeWith(SupplierVerificationStatus.Draft, "D-1"),
                                    MakeWith(SupplierVerificationStatus.PendingReview, "P-1"),
                                    MakeWith(SupplierVerificationStatus.Verified, "V-1"),
                                    MakeWith(SupplierVerificationStatus.Rejected, "R-1"));
            await ctx.SaveChangesAsync();
        }

        using (var ctx = CreateContext(dbName))
        {
            var repo = new SupplierRepository(ctx);
            var (pending, _) = await repo.ListForAdminAsync(
                new SupplierAdminFilter { Status = SupplierVerificationStatus.PendingReview }, 1, 25);

            Assert.That(pending.Count, Is.EqualTo(1));
            // Spec 026 — NormalizeLegalId strips non-alphanumerics, so "3-101-P-1" → "3101P1".
            Assert.That(pending.First().LegalId, Does.Contain("P1"));
        }
    }

    [Test]
    public async Task ListForAdminAsync_FiltersByLegalIdSubstring()
    {
        var dbName = $"sup-admin-lid-{Guid.NewGuid():N}";

        using (var ctx = CreateContext(dbName))
        {
            ctx.Suppliers.AddRange(MakeWith(SupplierVerificationStatus.Verified, "ABC123"),
                                    MakeWith(SupplierVerificationStatus.Verified, "XYZ999"),
                                    MakeWith(SupplierVerificationStatus.Verified, "ABC456"));
            await ctx.SaveChangesAsync();
        }

        using (var ctx = CreateContext(dbName))
        {
            var repo = new SupplierRepository(ctx);
            var (results, _) = await repo.ListForAdminAsync(
                new SupplierAdminFilter { LegalIdContains = "abc" }, 1, 25);

            Assert.That(results.Count, Is.EqualTo(2));
        }
    }

    [Test]
    public async Task ListForAdminAsync_FiltersByHasIncompleteCompliance()
    {
        var dbName = $"sup-admin-comp-{Guid.NewGuid():N}";

        using (var ctx = CreateContext(dbName))
        {
            // Spec 038 — "incomplete" now means any regulatory status is unreviewed (null).
            var fullyCompliant = MakeWith(SupplierVerificationStatus.Verified, "C-1");
            fullyCompliant.EditByAdmin("Compliant");
            fullyCompliant.ApplyRegulatoryEdit(
                HaciendaStatus.AlDia, CcssStatus.AlDia, SicopStatus.SinSanciones,
                false, false, null, "test-actor", DateTime.UtcNow);

            var incomplete = MakeWith(SupplierVerificationStatus.Verified, "C-2");
            incomplete.EditByAdmin("Incomplete");
            incomplete.ApplyRegulatoryEdit(
                HaciendaStatus.AlDia, null, null,
                false, false, null, "test-actor", DateTime.UtcNow);

            ctx.Suppliers.AddRange(fullyCompliant, incomplete);
            await ctx.SaveChangesAsync();
        }

        using (var ctx = CreateContext(dbName))
        {
            var repo = new SupplierRepository(ctx);
            var (results, _) = await repo.ListForAdminAsync(
                new SupplierAdminFilter { HasIncompleteCompliance = true }, 1, 25);

            Assert.That(results.Count, Is.EqualTo(1));
            // Spec 026 — NormalizeLegalId strips non-alphanumerics, so "3-101-C-2" → "3101C2".
            Assert.That(results.First().LegalId, Does.Contain("C2"));
        }
    }

    [Test]
    public async Task ListForAdminAsync_PaginatesResults()
    {
        var dbName = $"sup-admin-page-{Guid.NewGuid():N}";

        using (var ctx = CreateContext(dbName))
        {
            for (int i = 1; i <= 30; i++)
            {
                ctx.Suppliers.Add(MakeWith(SupplierVerificationStatus.Verified, $"P-{i:D3}"));
            }
            await ctx.SaveChangesAsync();
        }

        using (var ctx = CreateContext(dbName))
        {
            var repo = new SupplierRepository(ctx);
            var (page1, total) = await repo.ListForAdminAsync(
                new SupplierAdminFilter { Status = SupplierVerificationStatus.Verified }, page: 1, pageSize: 25);
            var (page2, _) = await repo.ListForAdminAsync(
                new SupplierAdminFilter { Status = SupplierVerificationStatus.Verified }, page: 2, pageSize: 25);

            Assert.That(total, Is.EqualTo(30));
            Assert.That(page1.Count, Is.EqualTo(25));
            Assert.That(page2.Count, Is.EqualTo(5));
        }
    }

    private static Supplier MakeWith(SupplierVerificationStatus status, string legalIdSuffix)
    {
        var s = Supplier.CreateDraft(
            legalId: $"3-101-{legalIdSuffix}",
            name: $"Supplier {legalIdSuffix}",
            createdByApplicantId: 1,
            firstBranchName: "Sede principal",
            firstBranchContactName: null, firstBranchEmail: null, firstBranchPhone: null,
            firstBranchAddressLine: null, firstBranchProvince: null,
            firstBranchShippingDetails: null, firstBranchWarrantyInfo: null);

        // Promote past Draft via the exposed lifecycle methods to get a non-Draft status.
        if (status == SupplierVerificationStatus.PendingReview)
        {
            s.SubmitForReview();
        }
        else if (status == SupplierVerificationStatus.Verified)
        {
            s.SubmitForReview();
            s.Verify("admin-sentinel");
        }
        else if (status == SupplierVerificationStatus.Rejected)
        {
            s.SubmitForReview();
            s.Reject("admin-sentinel", "test rejection");
        }
        return s;
    }
}
