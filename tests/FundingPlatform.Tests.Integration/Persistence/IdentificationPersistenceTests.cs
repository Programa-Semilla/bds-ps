using FundingPlatform.Application.Suppliers.DTOs;
using FundingPlatform.Application.Suppliers.Services;
using FundingPlatform.Domain.Entities;
using FundingPlatform.Domain.Enums;
using FundingPlatform.Infrastructure.Persistence;
using FundingPlatform.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace FundingPlatform.Tests.Integration.Persistence;

/// <summary>
/// Spec 026 — persistence round-trip of <see cref="IdentificationType"/> + canonical
/// legal ID, and the hyphenation-tolerant supplier lookup (FR-013).
///
/// SCOPE: uses the EF InMemory provider for parity with the rest of this project
/// (see <see cref="QuotationCreateCrcTests"/>). The real SQL TINYINT column and
/// UNIQUE index are exercised by the AspireFixture E2E suite (T022/T023/T028).
/// </summary>
[TestFixture]
public class IdentificationPersistenceTests
{
    private static AppDbContext CreateContext(string dbName) =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .ConfigureWarnings(w => w.Ignore(
                Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    [Test]
    public async Task Applicant_IdentificationTypeAndCanonicalLegalId_RoundTrip()
    {
        var dbName = $"ident-app-{Guid.NewGuid():N}";
        int applicantId;

        using (var ctx = CreateContext(dbName))
        {
            // Raw, unhyphenated cédula física — the VO canonicalises to 1-2345-6789.
            var applicant = new Applicant(
                userId: $"user-{Guid.NewGuid():N}",
                legalId: "123456789",
                firstName: "Ana", lastName: "Pérez",
                email: "ana@example.com", phone: null, performanceScore: null,
                identificationType: IdentificationType.CedulaFisica);
            ctx.Applicants.Add(applicant);
            await ctx.SaveChangesAsync();
            applicantId = applicant.Id;

            // Stored value is canonical immediately.
            Assert.That(applicant.LegalId, Is.EqualTo("1-2345-6789"));
        }

        using (var ctx = CreateContext(dbName))
        {
            var loaded = await ctx.Applicants.FirstAsync(a => a.Id == applicantId);

            Assert.That(loaded.IdentificationType, Is.EqualTo(IdentificationType.CedulaFisica));
            Assert.That(loaded.LegalId, Is.EqualTo("1-2345-6789"));
        }
    }

    [Test]
    public async Task Supplier_IdentificationType_RoundTrips()
    {
        var dbName = $"ident-sup-{Guid.NewGuid():N}";
        int supplierId;

        using (var ctx = CreateContext(dbName))
        {
            var supplier = Supplier.CreateDraft(
                legalId: "3101123456",
                name: "Proveedora S.A.",
                createdByApplicantId: 1,
                firstBranchName: "Sede principal",
                firstBranchContactName: null,
                firstBranchEmail: null,
                firstBranchPhone: null,
                firstBranchAddressLine: null,
                firstBranchProvince: null,
                firstBranchShippingDetails: null,
                firstBranchWarrantyInfo: null,
                identificationType: IdentificationType.Nite);
            ctx.Suppliers.Add(supplier);
            await ctx.SaveChangesAsync();
            supplierId = supplier.Id;

            Assert.That(supplier.LegalId, Is.EqualTo("3-101-123456"));
        }

        using (var ctx = CreateContext(dbName))
        {
            var loaded = await ctx.Suppliers.FirstAsync(s => s.Id == supplierId);
            Assert.That(loaded.IdentificationType, Is.EqualTo(IdentificationType.Nite));
            Assert.That(loaded.LegalId, Is.EqualTo("3-101-123456"));
        }
    }

    [TestCase("3101123456")]
    [TestCase("3-101-123456")]
    [TestCase("3 101 123456")]
    public async Task SupplierLookup_IsHyphenationTolerant(string query)
    {
        var dbName = $"ident-lookup-{Guid.NewGuid():N}";

        using (var ctx = CreateContext(dbName))
        {
            // Stored canonical via the jurídica type; Verified so the lookup is a Hit
            // for any applicant.
            var supplier = Supplier.CreateDraft(
                legalId: "3-101-123456",
                name: "Proveedora S.A.",
                createdByApplicantId: 1,
                firstBranchName: "Sede principal",
                firstBranchContactName: null,
                firstBranchEmail: null,
                firstBranchPhone: null,
                firstBranchAddressLine: null,
                firstBranchProvince: null,
                firstBranchShippingDetails: null,
                firstBranchWarrantyInfo: null,
                identificationType: IdentificationType.CedulaJuridica);
            supplier.SubmitForReview();
            supplier.Verify("admin-1");
            ctx.Suppliers.Add(supplier);
            await ctx.SaveChangesAsync();
        }

        using (var ctx = CreateContext(dbName))
        {
            var service = new SupplierCatalogService(
                new SupplierRepository(ctx),
                new ApplicationRepository(ctx),
                NullLogger<SupplierCatalogService>.Instance);

            var result = await service.SearchByLegalIdAsync(query, currentApplicantId: 99);

            Assert.That(result.Outcome, Is.EqualTo(SupplierLookupOutcome.Hit), $"query '{query}' should resolve to the stored supplier");
            Assert.That(result.Supplier!.LegalId, Is.EqualTo("3-101-123456"));
        }
    }
}
