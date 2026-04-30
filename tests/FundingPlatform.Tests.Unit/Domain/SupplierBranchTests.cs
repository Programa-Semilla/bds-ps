using FundingPlatform.Domain.Entities;

namespace FundingPlatform.Tests.Unit.Domain;

[TestFixture]
public class SupplierBranchTests
{
    [Test]
    public void AddBranch_TrimsAndStoresFields()
    {
        var s = Supplier.CreateDraft("3-101-1", "ACME", 1, "Sede principal",
            null, null, null, null, null, null, null);

        var b = s.AddBranch(
            "  Sede norte  ", "  Pedro ", " p@x.com ", " 8888-8888 ",
            " 50 m sur del parque ", "San José", "Envío en 3 días", "1 año", 1);

        Assert.That(b.BranchName, Is.EqualTo("Sede norte"));
        Assert.That(b.ContactName, Is.EqualTo("Pedro"));
        Assert.That(b.Email, Is.EqualTo("p@x.com"));
        Assert.That(b.Phone, Is.EqualTo("8888-8888"));
        Assert.That(b.AddressLine, Is.EqualTo("50 m sur del parque"));
        Assert.That(b.Province, Is.EqualTo("San José"));
        Assert.That(b.ShippingDetails, Is.EqualTo("Envío en 3 días"));
        Assert.That(b.WarrantyInfo, Is.EqualTo("1 año"));
        Assert.That(b.CreatedByApplicantId, Is.EqualTo(1));
        Assert.That(b.IsDefault, Is.False);
        Assert.That(b.CreatedAt, Is.LessThanOrEqualTo(DateTime.UtcNow));
    }

    [Test]
    public void EditBranch_UpdatesFieldsAndTimestamp()
    {
        var s = Supplier.CreateDraft("3-101-1", "ACME", 1, "Sede principal",
            "Ana", "ana@x.com", null, null, null, null, null);

        var defaultBranch = s.Branches.First();
        var beforeUpdate = defaultBranch.UpdatedAt;
        Thread.Sleep(10);

        s.EditBranch(defaultBranch.Id, "Sede principal renombrada", "Ana M", "ana@y.com",
            "8888-9999", "Nueva dirección", "Heredia", null, null);

        Assert.That(defaultBranch.BranchName, Is.EqualTo("Sede principal renombrada"));
        Assert.That(defaultBranch.ContactName, Is.EqualTo("Ana M"));
        Assert.That(defaultBranch.Email, Is.EqualTo("ana@y.com"));
        Assert.That(defaultBranch.UpdatedAt, Is.GreaterThan(beforeUpdate));
    }
}
