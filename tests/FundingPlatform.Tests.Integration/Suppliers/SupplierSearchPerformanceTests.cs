// Spec 021 — see specs/021-feedback-session-may13/tasks.md T133 and spec.md
// FR-009, NFR-006, SC-007.

using System.Diagnostics;
using FundingPlatform.Application.Suppliers.Queries;
using FundingPlatform.Domain.Entities;
using FundingPlatform.Infrastructure.Persistence;
using FundingPlatform.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace FundingPlatform.Tests.Integration.Suppliers;

/// <summary>
/// Spec 021 / US6 / T133 / FR-009 / NFR-006 / SC-007 — pragmatic smoke
/// performance check for the admin supplier autocomplete. Seeds 200 suppliers
/// and asserts P95 latency stays under 300 ms over 20 calls. Runs in CI
/// without warm-up; treats slow CI nodes as the worst case the platform must
/// tolerate.
///
/// SCOPE NOTE: uses the EF InMemory provider per the repo's Integration test
/// convention. The InMemory provider's <c>EF.Functions.Like</c> support is
/// case-sensitive substring matching, which is enough to validate the
/// handler's wire shape + the LINQ query plan compiles. Real SQL Server LIKE
/// latency (with the existing Suppliers.Name + Suppliers.LegalId indexes) is
/// exercised at the E2E layer.
/// </summary>
[TestFixture]
public class SupplierSearchPerformanceTests
{
    private const int SeedCount = 200;
    private const int Iterations = 20;
    private const int P95TargetMilliseconds = 300;

    private static AppDbContext CreateContext(string dbName) =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .ConfigureWarnings(w => w.Ignore(
                Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    [Test]
    public async Task SearchSuppliersForAdmin_P95Under300Ms_AtSeedScale()
    {
        var dbName = $"sup-perf-{Guid.NewGuid():N}";

        // Arrange — seed 200 suppliers. Half match the search term so the
        // result set caps at the 25-row limit (representative of the worst
        // case where the LIKE has to consider every row).
        using (var ctx = CreateContext(dbName))
        {
            for (var i = 0; i < SeedCount; i++)
            {
                var name = i % 2 == 0 ? $"PSCR Supplier {i:D3}" : $"Other Vendor {i:D3}";
                var supplier = Supplier.CreateDraft(
                    legalId: $"3-101-{i:D6}",
                    name: name,
                    createdByApplicantId: 1,
                    firstBranchName: "Sede principal",
                    firstBranchContactName: "Contact",
                    firstBranchEmail: null,
                    firstBranchPhone: null,
                    firstBranchAddressLine: null,
                    firstBranchProvince: "San Jose",
                    firstBranchShippingDetails: null,
                    firstBranchWarrantyInfo: null);
                ctx.Suppliers.Add(supplier);
            }
            await ctx.SaveChangesAsync();
        }

        // Act — time 20 iterations of the autocomplete handler.
        var timings = new List<long>(Iterations);
        using (var ctx = CreateContext(dbName))
        {
            var handler = new SearchSuppliersForAdminHandler(ctx);

            // One warm-up call so EF model + LINQ provider compile cost is
            // excluded from the P95 sample. Spec calls this a smoke test, not
            // a benchmark, but a single warm-up keeps CI noise sane.
            _ = await handler.HandleAsync(new SearchSuppliersForAdminQuery("PSCR"), CancellationToken.None);

            for (var i = 0; i < Iterations; i++)
            {
                var sw = Stopwatch.StartNew();
                var results = await handler.HandleAsync(
                    new SearchSuppliersForAdminQuery("PSCR"), CancellationToken.None);
                sw.Stop();
                timings.Add(sw.ElapsedMilliseconds);
                Assert.That(results, Is.Not.Empty,
                    "FR-009: seeded matches must produce at least one autocomplete row.");
            }
        }

        // Assert — P95 stays inside the 300 ms budget.
        timings.Sort();
        // P95 of a 20-sample series is the 19th element (0-indexed = 18).
        var p95Index = (int)Math.Ceiling(0.95 * Iterations) - 1;
        var p95 = timings[p95Index];

        Assert.That(p95, Is.LessThanOrEqualTo(P95TargetMilliseconds),
            $"NFR-006 / SC-007: P95 ≤ {P95TargetMilliseconds} ms @ {SeedCount} suppliers. " +
            $"Observed P95 = {p95} ms. Timings (sorted, ms): {string.Join(", ", timings)}");
    }
}
