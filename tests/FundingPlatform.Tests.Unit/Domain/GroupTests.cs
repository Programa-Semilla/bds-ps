using FundingPlatform.Domain.Entities;

namespace FundingPlatform.Tests.Unit.Domain;

/// <summary>Spec 016 / FR-001 / FR-006 — domain validation rules for the Group entity.</summary>
[TestFixture]
public class GroupTests
{
    [Test]
    public void Create_TrimsName()
    {
        var g = Group.Create("  Norte  ");
        Assert.That(g.Name, Is.EqualTo("Norte"));
    }

    [Test]
    public void Create_StampsCreatedAtAndUpdatedAt()
    {
        var before = DateTimeOffset.UtcNow.AddSeconds(-1);
        var g = Group.Create("Sur");
        var after = DateTimeOffset.UtcNow.AddSeconds(1);
        Assert.That(g.CreatedAt, Is.InRange(before, after));
        Assert.That(g.UpdatedAt, Is.InRange(before, after));
    }

    [TestCase("")]
    [TestCase("   ")]
    [TestCase("\t")]
    public void Create_RejectsEmptyOrWhitespace(string raw)
    {
        Assert.Throws<ArgumentException>(() => Group.Create(raw));
    }

    [Test]
    public void Create_RejectsOverLength()
    {
        var name = new string('x', Group.MaxNameLength + 1);
        Assert.Throws<ArgumentException>(() => Group.Create(name));
    }

    [Test]
    public void Create_AcceptsBoundaryLength()
    {
        var name = new string('x', Group.MaxNameLength);
        var g = Group.Create(name);
        Assert.That(g.Name, Is.EqualTo(name));
    }

    [Test]
    public void Rename_TrimsAndUpdatesName()
    {
        var g = Group.Create("Norte");
        g.Rename("  Norte Pacífico  ");
        Assert.That(g.Name, Is.EqualTo("Norte Pacífico"));
    }

    [Test]
    public void Rename_BumpsUpdatedAt()
    {
        var g = Group.Create("Norte");
        var stamp = g.UpdatedAt;
        // Sleep briefly so the new UpdatedAt is observably greater.
        Thread.Sleep(5);
        g.Rename("Norte Pacífico");
        Assert.That(g.UpdatedAt, Is.GreaterThan(stamp));
    }

    [Test]
    public void Rename_NoOp_PreservesUpdatedAt()
    {
        var g = Group.Create("Norte");
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
        var g = Group.Create("Norte");
        Assert.Throws<ArgumentException>(() => g.Rename(raw));
    }

    [Test]
    public void Rename_RejectsOverLength()
    {
        var g = Group.Create("Norte");
        Assert.Throws<ArgumentException>(() => g.Rename(new string('x', Group.MaxNameLength + 1)));
    }
}
