using FundingPlatform.Application.Disbursements;
using FundingPlatform.Domain.Entities;
using FundingPlatform.Domain.Enums;
using FundingPlatform.Tests.Integration.AiComparison;
using Microsoft.EntityFrameworkCore;
using static FundingPlatform.Tests.Integration.Disbursements.DisbursementTestFactory;

namespace FundingPlatform.Tests.Integration.Disbursements;

/// <summary>
/// Spec 045 / T044 / FR-030 — every mutating action writes exactly one <c>disbursement.*</c>
/// AdminAuditEvent with the actor and before/after payload.
/// </summary>
[TestFixture]
public class DisbursementAuditTests
{
    private static readonly DateOnly Today = new(2026, 7, 15);

    [Test]
    public async Task EveryMutation_WritesOneDisbursementAuditRow_WithActorAndPayload()
    {
        var db = $"disb-audit-{Guid.NewGuid():N}";
        var storage = new InMemoryObjectStorage();

        using var ctx = CreateContext(db);
        var appId = await SeedExecutedAppAsync(ctx);
        await SeedAllocationAsync(ctx, appId, 1_000_000m);
        var svc = NewService(ctx, storage);

        var rec = await svc.RecordAsync(new RecordDisbursementCommand(appId, Today, 200_000m, "TX-1", null), Actor, CancellationToken.None);
        var disbId = rec.Value;
        await svc.AttachEvidenceAsync(Ev(appId, disbId, EvidenceKind.BankReceipt, 200_000m), Actor, CancellationToken.None);
        await svc.AttachEvidenceAsync(Ev(appId, disbId, EvidenceKind.Invoice, 200_000m), Actor, CancellationToken.None);
        await svc.EditAsync(new EditDisbursementCommand(appId, disbId, Today, 200_000m, "TX-1b", null), Actor, CancellationToken.None);
        await svc.ValidateAsync(appId, disbId, Actor, CancellationToken.None);

        // A separate disbursement to exercise the cancel audit.
        var rec2 = await svc.RecordAsync(new RecordDisbursementCommand(appId, Today, 50_000m, "TX-2", null), Actor, CancellationToken.None);
        await svc.CancelAsync(appId, rec2.Value, Actor, CancellationToken.None);

        var rows = await ctx.AdminAuditEvents
            .Where(e => e.TargetType == AdminAuditEvent.TargetTypeDisbursement)
            .ToListAsync();

        var disb1Rows = rows.Where(r => r.TargetId == disbId.ToString()).ToList();
        var actionsForDisb1 = disb1Rows.Select(r => r.Action).ToList();
        var editedRow = disb1Rows.Single(r => r.Action == AdminAuditEvent.DisbursementEdited);
        Assert.Multiple(() =>
        {
            Assert.That(actionsForDisb1, Does.Contain(AdminAuditEvent.DisbursementRecorded));
            Assert.That(actionsForDisb1.Count(a => a == AdminAuditEvent.DisbursementEvidenceAttached), Is.EqualTo(2));
            Assert.That(actionsForDisb1, Does.Contain(AdminAuditEvent.DisbursementEdited));
            Assert.That(actionsForDisb1, Does.Contain(AdminAuditEvent.DisbursementValidated));
            Assert.That(rows.Where(r => r.TargetId == rec2.Value.ToString()).Select(r => r.Action),
                Does.Contain(AdminAuditEvent.DisbursementCancelled));
            Assert.That(rows.All(r => r.ActorUserId == Actor), Is.True, "every row carries the actor");
            // SC-007 / FR-030 — the edit payload carries BOTH before and after (not just after),
            // and the before block holds the prior value that changed.
            Assert.That(editedRow.PayloadJson, Does.Contain("before"));
            Assert.That(editedRow.PayloadJson, Does.Contain("after"));
            // Exact prior value (with closing quote) — distinguishes before "TX-1" from after "TX-1b".
            Assert.That(editedRow.PayloadJson, Does.Contain("\"bankTxn\":\"TX-1\""), "before-value of the changed bank reference");
        });
    }

    private static AttachDisbursementEvidenceCommand Ev(int appId, int disbId, EvidenceKind kind, decimal amount)
        => new(appId, disbId, kind, amount, "CRC", $"REF-{kind}", Today, Pdf(), $"{kind}.pdf", "application/pdf", 11);
}
