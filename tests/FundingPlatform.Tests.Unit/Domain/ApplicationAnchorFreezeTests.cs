using FundingPlatform.Domain.Entities;
using FundingPlatform.Domain.Enums;
using FundingPlatform.Domain.Exceptions;
using AppEntity = FundingPlatform.Domain.Entities.Application;

namespace FundingPlatform.Tests.Unit.Domain;

/// <summary>
/// Spec 029 / FR-017 / FR-021 — the Application Group anchor + the archived-Fund
/// freeze overlay. <see cref="Application.IsFrozen"/> derives from the loaded
/// <c>Group.Process.Fund.Status</c> chain; mutating methods throw
/// <see cref="FundArchivedException"/> when frozen.
/// </summary>
[TestFixture]
public class ApplicationAnchorFreezeTests
{
    private static void SetNav(object target, string prop, object value)
        => target.GetType().GetProperty(prop)!.SetValue(target, value);

    private static Category Cat() => new("Equipo", "desc", isActive: true);

    /// <summary>Builds an application whose anchored Fund has the given status.</summary>
    private static AppEntity AnchoredApp(FundStatus fundStatus)
    {
        var fund = Fund.Create("Fondo", "desc");
        if (fundStatus == FundStatus.Archived) fund.Archive();

        var process = Process.Create("Proceso", 1);
        SetNav(process, nameof(Process.Fund), fund);

        var group = Group.Create("Norte", 1);
        SetNav(group, nameof(Group.Process), process);

        var app = new AppEntity(applicantId: 1, groupId: 7, companyName: "Empresa");
        SetNav(app, nameof(AppEntity.Group), group);
        return app;
    }

    [TestCase(0)]
    [TestCase(-3)]
    public void Constructor_RejectsNonPositiveGroupId(int groupId)
        => Assert.Throws<ArgumentException>(() => new AppEntity(1, groupId, "Empresa"));

    [Test]
    public void IsFrozen_False_WhenFundActive()
    {
        var app = AnchoredApp(FundStatus.Active);
        Assert.That(app.IsFrozen, Is.False);
        // A mutating method on an active-Fund application succeeds.
        Assert.DoesNotThrow(() => app.SetCompanyName("Nueva Empresa"));
    }

    [Test]
    public void IsFrozen_True_WhenFundArchived()
    {
        var app = AnchoredApp(FundStatus.Archived);
        Assert.That(app.IsFrozen, Is.True);
    }

    [Test]
    public void MutatingMethods_Throw_WhenFundArchived()
    {
        var app = AnchoredApp(FundStatus.Archived);

        Assert.Throws<FundArchivedException>(() => app.SetCompanyName("Otra"));
        Assert.Throws<FundArchivedException>(() => app.AddItem(new Item("Servidor", Cat().Id, "specs")));
        Assert.Throws<FundArchivedException>(() => app.RemoveItem(1));
        Assert.Throws<FundArchivedException>(() => app.Submit(2));
        Assert.Throws<FundArchivedException>(() => app.RemoveByApplicant());
    }

    [Test]
    public void IsFrozen_False_WhenNavNotLoaded()
    {
        // Defense-in-depth: with no loaded nav chain the domain guard is inert
        // (the controller boundary guard is the primary enforcement).
        var app = new AppEntity(1, 7, "Empresa");
        Assert.That(app.IsFrozen, Is.False);
        Assert.DoesNotThrow(() => app.SetCompanyName("Otra"));
    }
}
