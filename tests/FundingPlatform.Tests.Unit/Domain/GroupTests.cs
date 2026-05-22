using FundingPlatform.Domain.Entities;

namespace FundingPlatform.Tests.Unit.Domain;

/// <summary>Spec 016 / 021 — domain validation rules for the Group entity
/// (FR-001 / FR-006). Spec 021 — every Group is attached to exactly one
/// Process at creation, and may be reparented via <see cref="Group.MoveToProcess"/>.</summary>
[TestFixture]
public class GroupTests
{
    private const int TestProcessId = 7;

    [Test]
    public void Create_TrimsName()
    {
        var g = Group.Create("  Norte  ", TestProcessId);
        Assert.That(g.Name, Is.EqualTo("Norte"));
    }

    [Test]
    public void Create_StampsCreatedAtAndUpdatedAt()
    {
        var before = DateTimeOffset.UtcNow.AddSeconds(-1);
        var g = Group.Create("Sur", TestProcessId);
        var after = DateTimeOffset.UtcNow.AddSeconds(1);
        Assert.That(g.CreatedAt, Is.InRange(before, after));
        Assert.That(g.UpdatedAt, Is.InRange(before, after));
    }

    [Test]
    public void Create_StoresProcessId()
    {
        var g = Group.Create("Norte", TestProcessId);
        Assert.That(g.ProcessId, Is.EqualTo(TestProcessId));
    }

    [TestCase(0)]
    [TestCase(-1)]
    public void Create_RejectsNonPositiveProcessId(int processId)
    {
        Assert.Throws<ArgumentException>(() => Group.Create("Norte", processId));
    }

    [TestCase("")]
    [TestCase("   ")]
    [TestCase("\t")]
    public void Create_RejectsEmptyOrWhitespace(string raw)
    {
        Assert.Throws<ArgumentException>(() => Group.Create(raw, TestProcessId));
    }

    [Test]
    public void Create_RejectsOverLength()
    {
        var name = new string('x', Group.MaxNameLength + 1);
        Assert.Throws<ArgumentException>(() => Group.Create(name, TestProcessId));
    }

    [Test]
    public void Create_AcceptsBoundaryLength()
    {
        var name = new string('x', Group.MaxNameLength);
        var g = Group.Create(name, TestProcessId);
        Assert.That(g.Name, Is.EqualTo(name));
    }

    [Test]
    public void MoveToProcess_ChangesProcessId_AndBumpsUpdatedAt()
    {
        var g = Group.Create("Norte", TestProcessId);
        var stamp = g.UpdatedAt;
        Thread.Sleep(5);
        g.MoveToProcess(TestProcessId + 1);
        Assert.That(g.ProcessId, Is.EqualTo(TestProcessId + 1));
        Assert.That(g.UpdatedAt, Is.GreaterThan(stamp));
    }

    [Test]
    public void MoveToProcess_NoOp_WhenSameProcess_PreservesUpdatedAt()
    {
        var g = Group.Create("Norte", TestProcessId);
        var stamp = g.UpdatedAt;
        Thread.Sleep(5);
        g.MoveToProcess(TestProcessId);
        Assert.That(g.UpdatedAt, Is.EqualTo(stamp));
    }

    [TestCase(0)]
    [TestCase(-3)]
    public void MoveToProcess_RejectsNonPositiveProcessId(int processId)
    {
        var g = Group.Create("Norte", TestProcessId);
        Assert.Throws<ArgumentException>(() => g.MoveToProcess(processId));
    }

    [Test]
    public void Rename_TrimsAndUpdatesName()
    {
        var g = Group.Create("Norte", TestProcessId);
        g.Rename("  Norte Pacífico  ");
        Assert.That(g.Name, Is.EqualTo("Norte Pacífico"));
    }

    [Test]
    public void Rename_BumpsUpdatedAt()
    {
        var g = Group.Create("Norte", TestProcessId);
        var stamp = g.UpdatedAt;
        // Sleep briefly so the new UpdatedAt is observably greater.
        Thread.Sleep(5);
        g.Rename("Norte Pacífico");
        Assert.That(g.UpdatedAt, Is.GreaterThan(stamp));
    }

    [Test]
    public void Rename_NoOp_PreservesUpdatedAt()
    {
        var g = Group.Create("Norte", TestProcessId);
        var stamp = g.UpdatedAt;
        Thread.Sleep(5);
        g.Rename("  Norte  "); // trimmed equal
        Assert.That(g.UpdatedAt, Is.EqualTo(stamp));
        Assert.That(g.Name, Is.EqualTo("Norte"));
    }

    [TestCase("")]
    [TestCase("   ")]
    public void Rename_RejectsEmptyOrWhitespace(string raw)
    {
        var g = Group.Create("Norte", TestProcessId);
        Assert.Throws<ArgumentException>(() => g.Rename(raw));
    }

    [Test]
    public void Rename_RejectsOverLength()
    {
        var g = Group.Create("Norte", TestProcessId);
        Assert.Throws<ArgumentException>(() => g.Rename(new string('x', Group.MaxNameLength + 1)));
    }
}
