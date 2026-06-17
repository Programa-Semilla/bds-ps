using FundingPlatform.Domain.Entities;
using AppEntity = FundingPlatform.Domain.Entities.Application;

namespace FundingPlatform.Tests.Unit.Domain;

/// <summary>
/// Spec 018 → 037 / FR-015 / FR-016 — entity-level company-name snapshot
/// invariants, exercised through the constructor and (since spec 037) the public
/// <see cref="AppEntity.SetCompany"/> draft re-select path. These rules belong on
/// the entity per Constitution II; the controller / service layers are thin pass-throughs.
/// </summary>
[TestFixture]
public class ApplicationCompanyNameTests
{
    [Test]
    public void Constructor_PersistsTrimmedCompanyName()
    {
        var app = new AppEntity(applicantId: 1, 1, null,companyName: "  Sazón Vegetariano  ");

        Assert.That(app.CompanyName, Is.EqualTo("Sazón Vegetariano"));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    [TestCase("\t\n  ")]
    public void Constructor_RejectsNullOrWhitespaceCompanyName(string? blank)
    {
        Assert.That(
            () => new AppEntity(applicantId: 1, 1, null,companyName: blank!),
            Throws.ArgumentException);
    }

    [Test]
    public void Constructor_AcceptsExactly200Characters()
    {
        var name = new string('a', 200);

        Assert.That(
            () => new AppEntity(applicantId: 1, 1, null,companyName: name),
            Throws.Nothing);
    }

    [Test]
    public void Constructor_RejectsOver200Characters()
    {
        var name = new string('a', 201);

        Assert.That(
            () => new AppEntity(applicantId: 1, 1, null,companyName: name),
            Throws.ArgumentException);
    }

    [Test]
    public void SetCompany_AcceptsExactly200CharsAfterTrim()
    {
        var app = new AppEntity(applicantId: 1, 1, null, companyName: "Initial");
        var name = "  " + new string('a', 200) + "  ";

        Assert.That(() => app.SetCompany(7, name), Throws.Nothing);
        Assert.That(app.CompanyName.Length, Is.EqualTo(200));
        Assert.That(app.CompanyId, Is.EqualTo(7));
    }

    [Test]
    public void SetCompany_BumpsUpdatedAtAndSnapshotsName()
    {
        var app = new AppEntity(applicantId: 1, 1, null, companyName: "Initial");
        var beforeTouch = DateTime.UtcNow.AddSeconds(-1);

        app.SetCompany(9, "Sazón Vegetariano");

        Assert.That(app.UpdatedAt, Is.GreaterThanOrEqualTo(beforeTouch));
        Assert.That(app.CompanyName, Is.EqualTo("Sazón Vegetariano"));
        Assert.That(app.CompanyId, Is.EqualTo(9));
    }
}
