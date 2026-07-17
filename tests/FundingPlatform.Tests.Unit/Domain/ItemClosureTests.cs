using FundingPlatform.Domain.Entities;
using FundingPlatform.Domain.Enums;

namespace FundingPlatform.Tests.Unit.Domain;

/// <summary>
/// Spec 047 / FR-016/FR-017 — the stored budget-line closure state + stamp/clear semantics, driven
/// through the aggregate root (<see cref="Application.CloseLine"/>/<see cref="Application.ReopenLine"/>,
/// the single entry points to the internal <c>Item.Close/Reopen</c>).
/// </summary>
[TestFixture]
public class ItemClosureTests
{
    private static (FundingPlatform.Domain.Entities.Application App, Item Item) NewAppWithLine()
    {
        var app = new FundingPlatform.Domain.Entities.Application(applicantId: 1, groupId: 1, companyId: null, companyName: "E");
        var item = new Item("Line", categoryId: 1);
        app.AddItem(item);
        return (app, item);
    }

    [Test]
    public void CloseLine_StampsClosed()
    {
        var (app, item) = NewAppWithLine();
        app.CloseLine(item.Id, "user-1", "done");

        Assert.That(item.ClosureState, Is.EqualTo(ItemClosureState.Closed));
        Assert.That(item.ClosedByUserId, Is.EqualTo("user-1"));
        Assert.That(item.ClosedAtUtc, Is.Not.Null);
        Assert.That(item.ClosureReason, Is.EqualTo("done"));
    }

    [Test]
    public void CloseLine_IsIdempotent()
    {
        var (app, item) = NewAppWithLine();
        app.CloseLine(item.Id, "user-1", null);
        app.CloseLine(item.Id, "user-2", "again"); // no-op

        Assert.That(item.ClosureState, Is.EqualTo(ItemClosureState.Closed));
        Assert.That(item.ClosedByUserId, Is.EqualTo("user-1")); // unchanged by the second call
    }

    [Test]
    public void ReopenLine_ClearsStamp_SetsReopenReason()
    {
        var (app, item) = NewAppWithLine();
        app.CloseLine(item.Id, "user-1", "done");
        app.ReopenLine(item.Id, "user-2", "correction");

        Assert.That(item.ClosureState, Is.EqualTo(ItemClosureState.Open));
        Assert.That(item.ClosedByUserId, Is.Null);
        Assert.That(item.ClosedAtUtc, Is.Null);
        Assert.That(item.ClosureReason, Is.Null);
        Assert.That(item.ReopenReason, Is.EqualTo("correction"));
    }
}
