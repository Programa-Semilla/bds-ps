using FundingPlatform.Domain.Entities;
using AppEntity = FundingPlatform.Domain.Entities.Application;

namespace FundingPlatform.Tests.Unit.Domain;

/// <summary>
/// Spec 037 / FR-001 — entity-level invariants for the <see cref="Company"/>
/// aggregate: name trim/required/≤200, rename no-op, archive/unarchive toggles.
/// The per-applicant uniqueness + last-active floor are service/DB concerns (D3/D5),
/// not exercised here.
/// </summary>
[TestFixture]
public class CompanyTests
{
    [Test]
    public void Constructor_TrimsName_AndIsActive()
    {
        var company = new Company(applicantId: 1, name: "  Acme Consulting S.A.  ");

        Assert.Multiple(() =>
        {
            Assert.That(company.Name, Is.EqualTo("Acme Consulting S.A."));
            Assert.That(company.ApplicantId, Is.EqualTo(1));
            Assert.That(company.IsActive, Is.True);
            Assert.That(company.ArchivedAt, Is.Null);
        });
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void Constructor_RejectsNullOrWhitespaceName(string? blank)
        => Assert.That(() => new Company(1, blank!), Throws.ArgumentException);

    [Test]
    public void Constructor_AcceptsExactly200Chars()
        => Assert.That(() => new Company(1, new string('a', 200)), Throws.Nothing);

    [Test]
    public void Constructor_RejectsOver200Chars()
        => Assert.That(() => new Company(1, new string('a', 201)), Throws.ArgumentException);

    [Test]
    public void Rename_ChangesName_AndBumpsUpdatedAt()
    {
        var company = new Company(1, "Old Name");
        var before = company.UpdatedAt;

        company.Rename("  New Name  ");

        Assert.That(company.Name, Is.EqualTo("New Name"));
        Assert.That(company.UpdatedAt, Is.GreaterThanOrEqualTo(before));
    }

    [Test]
    public void Rename_NoOp_WhenEqualAfterTrim()
    {
        var company = new Company(1, "Same");
        var before = company.UpdatedAt;

        company.Rename("  Same  ");

        Assert.That(company.Name, Is.EqualTo("Same"));
        // No-op leaves UpdatedAt untouched (the caller suppresses the audit row).
        Assert.That(company.UpdatedAt, Is.EqualTo(before));
    }

    [Test]
    public void Rename_RejectsOver200Chars()
    {
        var company = new Company(1, "Valid");
        Assert.That(() => company.Rename(new string('a', 201)), Throws.ArgumentException);
    }

    [Test]
    public void Archive_Then_Unarchive_TogglesIsActive()
    {
        var company = new Company(1, "Toggle Co");

        company.Archive();
        Assert.That(company.IsActive, Is.False);
        Assert.That(company.ArchivedAt, Is.Not.Null);

        company.Unarchive();
        Assert.That(company.IsActive, Is.True);
        Assert.That(company.ArchivedAt, Is.Null);
    }

    [Test]
    public void Archive_IsIdempotent()
    {
        var company = new Company(1, "Idem Co");
        company.Archive();
        var firstArchivedAt = company.ArchivedAt;

        company.Archive();

        Assert.That(company.ArchivedAt, Is.EqualTo(firstArchivedAt));
    }

    [Test]
    public void SetCompany_WhileDraft_ReSelectsSnapshot()
    {
        var app = new AppEntity(applicantId: 1, groupId: 1, companyId: 7, companyName: "Snapshot Co");
        // A fresh Draft re-selects freely (re-copies the snapshot).
        Assert.That(() => app.SetCompany(8, "Re-selected Co"), Throws.Nothing);
        Assert.That(app.CompanyId, Is.EqualTo(8));
        Assert.That(app.CompanyName, Is.EqualTo("Re-selected Co"));
    }

    [Test]
    public void SetCompany_AfterSubmit_Throws()
    {
        // Spec 037 / FR-015 — the company is frozen once the application leaves Draft.
        var app = new AppEntity(applicantId: 1, groupId: 1, companyId: 7, companyName: "Snapshot Co");
        ForceState(app, FundingPlatform.Domain.Enums.ApplicationState.Submitted);

        Assert.That(() => app.SetCompany(8, "Re-selected Co"),
            Throws.InstanceOf<InvalidOperationException>());
        // The snapshot + reference are unchanged.
        Assert.That(app.CompanyId, Is.EqualTo(7));
        Assert.That(app.CompanyName, Is.EqualTo("Snapshot Co"));
    }

    private static void ForceState(AppEntity app, FundingPlatform.Domain.Enums.ApplicationState state)
        => typeof(AppEntity).GetProperty(nameof(AppEntity.State))!.SetValue(app, state);
}
