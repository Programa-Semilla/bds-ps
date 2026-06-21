using System.Reflection;
using FundingPlatform.Application.Reviewer;
using FundingPlatform.Application.Services;
using FundingPlatform.Domain.Entities;
using FundingPlatform.Domain.Enums;
using FundingPlatform.Domain.Interfaces;
using FundingPlatform.Domain.ValueObjects;
using NSubstitute;
using AppEntity = FundingPlatform.Domain.Entities.Application;

namespace FundingPlatform.Tests.Integration.Audit;

/// <summary>
/// Spec 040 / FR-006 — the auditor inbox row surfaces a provider warning indicator and the
/// time the application entered audit. Verified at the projection level with a stubbed
/// repository (the EF includes are exercised by the repository's own integration coverage).
/// </summary>
[TestFixture]
public class AuditorInboxIndicatorTests
{
    [Test]
    public async Task Inbox_FlagsProviderWarning_AndEnteredAuditTime_FromVersionHistory()
    {
        var app = new AppEntity(applicantId: 1, groupId: 1, companyId: null, companyName: "ACME");
        typeof(AppEntity).GetProperty("Id")!.SetValue(app, 42);
        typeof(AppEntity).GetProperty("State")!.SetValue(app, ApplicationState.PendingAudit);

        // Item with a quotation whose supplier carries an admin-set regulatory warning.
        var item = new Item("Producto", 1);
        var supplier = (Supplier)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(Supplier));
        typeof(Supplier).GetProperty(nameof(Supplier.HasWarning))!.SetValue(supplier, true);
        var quotation = new Quotation(
            supplierId: 7, supplierBranchId: 1, documentId: 1, price: 100m,
            validUntil: new DateOnly(2030, 1, 1), currency: "CRC",
            deliveryLeadTime: new TimeDuration(5, DurationUnit.Days),
            warranty: new TimeDuration(12, DurationUnit.Months));
        typeof(Quotation).GetProperty(nameof(Quotation.Supplier))!.SetValue(quotation, supplier);
        var quotations = (System.Collections.IList)typeof(Item)
            .GetField("_quotations", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(item)!;
        quotations.Add(quotation);
        app.AddItem(item);

        // A SentToAudit history entry sets the entered-audit time.
        var entered = new DateTime(2026, 6, 18, 9, 30, 0, DateTimeKind.Utc);
        var vh = new VersionHistory("reviewer-1", "SentToAudit", "Enviado a auditoría");
        typeof(VersionHistory).GetProperty(nameof(VersionHistory.Timestamp))!.SetValue(vh, entered);
        app.AddVersionHistory(vh);

        var repo = Substitute.For<IApplicationRepository>();
        repo.GetPendingAuditInboxAsync(Arg.Any<ReviewerScopeHint>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<string?>())
            .Returns((new List<AppEntity> { app }, 1));

        var projection = new AuditorQueueProjection(repo);
        var rows = await projection.GetInboxAsync(ReviewerScope.Admin, null, 1, 50, CancellationToken.None);

        Assert.That(rows, Has.Count.EqualTo(1));
        Assert.That(rows[0].HasProviderWarning, Is.True, "FR-006: provider warning indicator on the inbox row.");
        Assert.That(rows[0].EnteredAuditAtUtc, Is.EqualTo(entered), "FR-006: entered-audit time from the SentToAudit history.");
        Assert.That(rows[0].ItemCount, Is.EqualTo(1));
    }
}
