using FundingPlatform.Domain.Entities;
using FundingPlatform.Domain.Enums;

namespace FundingPlatform.Tests.Unit.Domain;

/// <summary>
/// Spec 029 / FR-001 / FR-005 / FR-006 — domain invariants for the
/// <see cref="Fund"/> aggregate: lifecycle (Active/Archived) + the optional
/// regulation reference (all-or-nothing), plus name/description validation.
/// </summary>
[TestFixture]
public class FundTests
{
    private static Fund NewFund() => Fund.Create("Fondo General", "Descripción del fondo.");

    [Test]
    public void Create_ReturnsActiveFund_TrimsAndValidates()
    {
        var before = DateTimeOffset.UtcNow.AddSeconds(-1);

        var fund = Fund.Create("  Fondo X  ", "  Una descripción.  ");

        Assert.That(fund.Name, Is.EqualTo("Fondo X"));
        Assert.That(fund.Description, Is.EqualTo("Una descripción."));
        Assert.That(fund.Status, Is.EqualTo(FundStatus.Active));
        Assert.That(fund.HasRegulation, Is.False);
        Assert.That(fund.CreatedAt, Is.GreaterThanOrEqualTo(before));
    }

    [TestCase("")]
    [TestCase("   ")]
    [TestCase(null)]
    public void Create_RejectsEmptyName(string? raw)
        => Assert.Throws<ArgumentException>(() => Fund.Create(raw!, "desc"));

    [TestCase("")]
    [TestCase("   ")]
    [TestCase(null)]
    public void Create_RejectsEmptyDescription(string? raw)
        => Assert.Throws<ArgumentException>(() => Fund.Create("Fondo", raw!));

    [Test]
    public void Create_RejectsOverLengthName()
        => Assert.Throws<ArgumentException>(
            () => Fund.Create(new string('x', Fund.MaxNameLength + 1), "desc"));

    [Test]
    public void Create_RejectsOverLengthDescription()
        => Assert.Throws<ArgumentException>(
            () => Fund.Create("Fondo", new string('x', Fund.MaxDescriptionLength + 1)));

    [Test]
    public void Archive_Then_Reactivate_AreIdempotent()
    {
        var fund = NewFund();

        fund.Archive();
        Assert.That(fund.Status, Is.EqualTo(FundStatus.Archived));
        fund.Archive(); // idempotent — no throw
        Assert.That(fund.Status, Is.EqualTo(FundStatus.Archived));

        fund.Reactivate();
        Assert.That(fund.Status, Is.EqualTo(FundStatus.Active));
        fund.Reactivate(); // idempotent
        Assert.That(fund.Status, Is.EqualTo(FundStatus.Active));
    }

    [Test]
    public void Rename_And_EditDescription_RejectedWhileArchived()
    {
        var fund = NewFund();
        fund.Archive();

        Assert.Throws<InvalidOperationException>(() => fund.Rename("Otro"));
        Assert.Throws<InvalidOperationException>(() => fund.EditDescription("Otra"));
    }

    [Test]
    public void SetRegulation_SetsAllColumns_HasRegulationTrue()
    {
        var fund = NewFund();
        var now = DateTime.UtcNow;

        fund.SetRegulation("blob/key.pdf", "reglamento.pdf", "application/pdf", 1234, "user-1", now);

        Assert.Multiple(() =>
        {
            Assert.That(fund.HasRegulation, Is.True);
            Assert.That(fund.RegulationBlobKey, Is.EqualTo("blob/key.pdf"));
            Assert.That(fund.RegulationFileName, Is.EqualTo("reglamento.pdf"));
            Assert.That(fund.RegulationContentType, Is.EqualTo("application/pdf"));
            Assert.That(fund.RegulationSizeBytes, Is.EqualTo(1234));
            Assert.That(fund.RegulationUploadedByUserId, Is.EqualTo("user-1"));
            Assert.That(fund.RegulationUploadedAtUtc, Is.EqualTo(now));
        });
    }

    [Test]
    public void RemoveRegulation_ClearsAllColumns()
    {
        var fund = NewFund();
        fund.SetRegulation("blob/key.pdf", "reglamento.pdf", "application/pdf", 1234, "user-1", DateTime.UtcNow);

        fund.RemoveRegulation();

        Assert.Multiple(() =>
        {
            Assert.That(fund.HasRegulation, Is.False);
            Assert.That(fund.RegulationBlobKey, Is.Null);
            Assert.That(fund.RegulationFileName, Is.Null);
            Assert.That(fund.RegulationContentType, Is.Null);
            Assert.That(fund.RegulationSizeBytes, Is.Null);
            Assert.That(fund.RegulationUploadedByUserId, Is.Null);
            Assert.That(fund.RegulationUploadedAtUtc, Is.Null);
        });
    }

    [TestCase("", "f.pdf", "application/pdf", 1L)]
    [TestCase("k", "", "application/pdf", 1L)]
    [TestCase("k", "f.pdf", "", 1L)]
    [TestCase("k", "f.pdf", "application/pdf", 0L)] // ArgumentOutOfRangeException : ArgumentException
    public void SetRegulation_RejectsInvalidArguments(string key, string name, string type, long size)
    {
        var fund = NewFund();
        Assert.Throws(Is.AssignableTo<ArgumentException>(),
            () => fund.SetRegulation(key, name, type, size, "user-1", DateTime.UtcNow));
        Assert.That(fund.HasRegulation, Is.False, "no partial regulation state on rejection");
    }
}
