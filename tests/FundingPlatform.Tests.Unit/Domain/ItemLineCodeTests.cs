using FundingPlatform.Domain.Entities;
using AppEntity = FundingPlatform.Domain.Entities.Application;

namespace FundingPlatform.Tests.Unit.Domain;

/// <summary>
/// Spec 018 / FR-012 / FR-013 / FR-014 — entity-level invariants for line codes.
/// Per Constitution II + R-008, the aggregate root <see cref="AppEntity"/>
/// composes the per-Application uniqueness check with the field-level write on
/// <c>Item.AssignLineCode</c>. We exercise both via the public
/// <see cref="AppEntity.AssignLineCodeToItem"/> entry point.
/// </summary>
[TestFixture]
public class ItemLineCodeTests
{
    private static AppEntity BuildAppWithItems(int itemCount)
    {
        var app = new AppEntity(applicantId: 1, 1, companyName: "Test Company");
        for (var i = 0; i < itemCount; i++)
        {
            var item = new Item($"Product-{i + 1}", categoryId: 1);
            // The synthetic Id is normally assigned by the DB; the in-memory tests
            // need it set so AssignLineCodeToItem can locate the item.
            typeof(Item).GetProperty("Id")!.SetValue(item, i + 1);
            app.AddItem(item);
        }
        return app;
    }

    [Test]
    public void AssignLineCodeToItem_PersistsTrimmedValue()
    {
        var app = BuildAppWithItems(1);

        app.AssignLineCodeToItem(itemId: 1, lineCode: "  T1-1  ");

        Assert.That(app.Items[0].LineCode, Is.EqualTo("T1-1"));
    }

    [TestCase("")]
    [TestCase("   ")]
    public void AssignLineCodeToItem_RejectsBlank(string blank)
    {
        var app = BuildAppWithItems(1);

        Assert.That(
            () => app.AssignLineCodeToItem(itemId: 1, lineCode: blank),
            Throws.ArgumentException);
    }

    [Test]
    public void AssignLineCodeToItem_AcceptsExactly16Chars()
    {
        var app = BuildAppWithItems(1);
        var code = new string('A', 16);

        Assert.That(
            () => app.AssignLineCodeToItem(itemId: 1, lineCode: code),
            Throws.Nothing);
        Assert.That(app.Items[0].LineCode, Is.EqualTo(code));
    }

    [Test]
    public void AssignLineCodeToItem_RejectsOver16Chars()
    {
        var app = BuildAppWithItems(1);
        var code = new string('A', 17);

        Assert.That(
            () => app.AssignLineCodeToItem(itemId: 1, lineCode: code),
            Throws.ArgumentException);
    }

    [Test]
    public void AssignLineCodeToItem_RejectsDuplicateWithinApplication()
    {
        var app = BuildAppWithItems(2);
        app.AssignLineCodeToItem(itemId: 1, lineCode: "T1-1");

        Assert.That(
            () => app.AssignLineCodeToItem(itemId: 2, lineCode: "T1-1"),
            Throws.InvalidOperationException
                .With.Message.Contains("already assigned"));
    }

    [Test]
    public void AssignLineCodeToItem_AllowsDistinctCodesAcrossSiblings()
    {
        var app = BuildAppWithItems(3);
        app.AssignLineCodeToItem(itemId: 1, lineCode: "T1-1");
        app.AssignLineCodeToItem(itemId: 2, lineCode: "T1-2");
        app.AssignLineCodeToItem(itemId: 3, lineCode: "T1-3");

        Assert.That(app.Items.Select(i => i.LineCode),
            Is.EquivalentTo(new[] { "T1-1", "T1-2", "T1-3" }));
    }

    [Test]
    public void AssignLineCodeToItem_AllowsReassignmentOfSameItem()
    {
        var app = BuildAppWithItems(1);
        app.AssignLineCodeToItem(itemId: 1, lineCode: "T1-1");

        // Re-assigning the same item to the same code is idempotent — uniqueness
        // is checked against sibling items only.
        Assert.That(
            () => app.AssignLineCodeToItem(itemId: 1, lineCode: "T1-1"),
            Throws.Nothing);

        // And re-assigning to a different code replaces the value.
        app.AssignLineCodeToItem(itemId: 1, lineCode: "T1-A");
        Assert.That(app.Items[0].LineCode, Is.EqualTo("T1-A"));
    }

    [Test]
    public void AssignLineCodeToItem_ThrowsWhenItemNotFound()
    {
        var app = BuildAppWithItems(1);

        Assert.That(
            () => app.AssignLineCodeToItem(itemId: 999, lineCode: "T1-1"),
            Throws.InvalidOperationException
                .With.Message.Contains("999"));
    }

    [Test]
    public void AssignLineCodeToItem_CaseSensitiveUniquenessCheck()
    {
        // Per data-model.md "case-sensitive" — "T1-1" and "t1-1" are distinct.
        var app = BuildAppWithItems(2);
        app.AssignLineCodeToItem(itemId: 1, lineCode: "T1-1");

        Assert.That(
            () => app.AssignLineCodeToItem(itemId: 2, lineCode: "t1-1"),
            Throws.Nothing);
    }
}
