using FundingPlatform.Domain.Entities;
using FundingPlatform.Domain.Enums;

namespace FundingPlatform.Tests.Unit.Domain;

/// <summary>
/// Spec 032 — the admin-assigned <see cref="Applicant.UserCode"/> is trimmed,
/// whitespace-only collapses to <c>null</c> (unassigned), and values longer than
/// 50 characters are rejected. Uniqueness is enforced by the service + DB index,
/// not the entity, so it is not covered here.
/// </summary>
[TestFixture]
public class ApplicantUserCodeTests
{
    private static Applicant NewApplicant(string? userCode) => new(
        userId: "user-1",
        legalId: "1-1234-5678",
        firstName: "Ana",
        lastName: "Mora",
        email: "ana@programa-semilla.test",
        phone: null,
        performanceScore: null,
        identificationType: IdentificationType.CedulaFisica,
        userCode: userCode);

    [Test]
    public void Ctor_WithValidUserCode_StoresTrimmedValue()
    {
        var applicant = NewApplicant("  ABC-001  ");

        Assert.That(applicant.UserCode, Is.EqualTo("ABC-001"));
    }

    [Test]
    public void Ctor_WithNullOrWhitespaceUserCode_StoresNull()
    {
        Assert.That(NewApplicant(null).UserCode, Is.Null);
        Assert.That(NewApplicant("   ").UserCode, Is.Null);
    }

    [Test]
    public void Ctor_WithUserCodeAtMaxLength_IsAccepted()
    {
        var fifty = new string('X', 50);

        Assert.That(NewApplicant(fifty).UserCode, Is.EqualTo(fifty));
    }

    [Test]
    public void Ctor_WithUserCodeOver50Chars_Throws()
    {
        Assert.Throws<ArgumentException>(() => NewApplicant(new string('X', 51)));
    }

    [Test]
    public void UpdateProfile_SetsAndClearsUserCode()
    {
        var applicant = NewApplicant("ABC-001");

        applicant.UpdateProfile(
            legalId: "1-1234-5678",
            firstName: "Ana",
            lastName: "Mora",
            email: "ana@programa-semilla.test",
            phone: null,
            identificationType: IdentificationType.CedulaFisica,
            userCode: "  XYZ-9 ");
        Assert.That(applicant.UserCode, Is.EqualTo("XYZ-9"));

        applicant.UpdateProfile(
            legalId: "1-1234-5678",
            firstName: "Ana",
            lastName: "Mora",
            email: "ana@programa-semilla.test",
            phone: null,
            identificationType: IdentificationType.CedulaFisica,
            userCode: "   ");
        Assert.That(applicant.UserCode, Is.Null);
    }
}
