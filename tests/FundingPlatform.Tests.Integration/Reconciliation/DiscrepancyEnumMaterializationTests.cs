using FundingPlatform.Domain.Entities;
using FundingPlatform.Domain.Enums;
using FundingPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FundingPlatform.Tests.Integration.Reconciliation;

/// <summary>
/// Spec 048 / T013 — round-trips each TINYINT enum column (<c>ScopeType</c>/<c>Comparison</c>/
/// <c>Severity</c>/<c>State</c>) through the EF <c>HasConversion&lt;byte&gt;()</c> mappings, plus the
/// append-only <see cref="DiscrepancyEvent"/> chain's <c>FromState</c>/<c>ToState</c>. On the InMemory
/// provider this proves the value mapping; the <b>real-SQL Byte→Int32 materialization</b> that InMemory
/// hides (the 035/040/045 lesson), the <c>UX_Discrepancies_Identity</c> unique constraint, and the
/// <c>DiscrepancyEvents</c> CASCADE are exercised by the E2E suite against SQL Server (every
/// Reconciliation persist/list/detail materializes these columns from a real database).
/// </summary>
[TestFixture]
public class DiscrepancyEnumMaterializationTests
{
    private const string SystemActor = "system-sentinel";
    private const string Operator = "finop-1";
    private static readonly DateTimeOffset Now = new(2026, 7, 17, 12, 0, 0, TimeSpan.Zero);

    private static AppDbContext CreateContext(string dbName) =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .ConfigureWarnings(w => w.Ignore(
                Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    [Test]
    public async Task AllFourEnumsAndEventChain_RoundTrip_ThroughByteConversion()
    {
        var db = $"disc-enum-{Guid.NewGuid():N}";
        using var ctx = CreateContext(db);

        // A blocking discrepancy that stays Open (line over-payment).
        var blocking = Discrepancy.Detect(
            applicationId: 42, DiscrepancyScopeType.BudgetLine, scopeEntityId: 7,
            ReconciliationComparison.LinePaymentVsBudget, DiscrepancySeverity.Blocking,
            expected: 100_000m, actual: 150_000m, toleranceApplied: 0m, sourceDocument: "línea",
            detectedByUserId: SystemActor, nowUtc: Now);

        // A warning discrepancy assigned then waived (evidence date anomaly).
        var waived = Discrepancy.Detect(
            applicationId: 42, DiscrepancyScopeType.Document, scopeEntityId: 9,
            ReconciliationComparison.EvidenceDateAnomaly, DiscrepancySeverity.Warning,
            expected: 0m, actual: 0m, toleranceApplied: 0m, sourceDocument: "factura",
            detectedByUserId: SystemActor, nowUtc: Now);
        waived.Assign(Operator, Operator, Now);
        waived.Waive("Fecha corregida en origen; aceptada.", Operator, Now);

        // A warning discrepancy that auto-resolved (possible duplicate payment cleared).
        var resolved = Discrepancy.Detect(
            applicationId: 42, DiscrepancyScopeType.Payment, scopeEntityId: 3,
            ReconciliationComparison.PossibleDuplicatePayment, DiscrepancySeverity.Warning,
            expected: 500_000m, actual: 500_000m, toleranceApplied: 0m, sourceDocument: "pago",
            detectedByUserId: SystemActor, nowUtc: Now);
        resolved.AutoResolve(SystemActor, Now);

        ctx.Discrepancies.AddRange(blocking, waived, resolved);
        await ctx.SaveChangesAsync();

        var storedBlocking = await ctx.Discrepancies.AsNoTracking()
            .SingleAsync(d => d.ScopeType == DiscrepancyScopeType.BudgetLine);
        var storedWaived = await ctx.Discrepancies.AsNoTracking().Include(d => d.Events)
            .SingleAsync(d => d.ScopeType == DiscrepancyScopeType.Document);
        var storedResolved = await ctx.Discrepancies.AsNoTracking()
            .SingleAsync(d => d.ScopeType == DiscrepancyScopeType.Payment);

        Assert.Multiple(() =>
        {
            Assert.That(storedBlocking.Severity, Is.EqualTo(DiscrepancySeverity.Blocking));
            Assert.That(storedBlocking.State, Is.EqualTo(DiscrepancyState.Open));
            Assert.That(storedBlocking.Comparison, Is.EqualTo(ReconciliationComparison.LinePaymentVsBudget));
            Assert.That(storedBlocking.Difference, Is.EqualTo(50_000m));

            Assert.That(storedWaived.Severity, Is.EqualTo(DiscrepancySeverity.Warning));
            Assert.That(storedWaived.State, Is.EqualTo(DiscrepancyState.Waived));
            Assert.That(storedWaived.Comparison, Is.EqualTo(ReconciliationComparison.EvidenceDateAnomaly));
            Assert.That(storedWaived.WaivedReason, Is.Not.Null);
            // Opened → Assigned → Waived
            Assert.That(storedWaived.Events, Has.Count.EqualTo(3));
            Assert.That(storedWaived.Events[^1].ToState, Is.EqualTo(DiscrepancyState.Waived));
            Assert.That(storedWaived.Events[^1].Kind, Is.EqualTo(DiscrepancyEvent.KindWaived));

            Assert.That(storedResolved.Severity, Is.EqualTo(DiscrepancySeverity.Warning));
            Assert.That(storedResolved.State, Is.EqualTo(DiscrepancyState.Resolved));
            Assert.That(storedResolved.Comparison, Is.EqualTo(ReconciliationComparison.PossibleDuplicatePayment));
            Assert.That(storedResolved.ResolvedAt, Is.Not.Null);
        });
    }
}
