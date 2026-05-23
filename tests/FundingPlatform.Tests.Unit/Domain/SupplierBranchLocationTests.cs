using System.Reflection;
using FundingPlatform.Domain.Entities;

namespace FundingPlatform.Tests.Unit.Domain;

/// <summary>
/// Spec 025 / FR-006 — the extended 3-tier <see cref="SupplierBranch.SetLocation"/>
/// invariant (Provincia → Cantón → Distrito). Verifies the superset rules over the
/// spec-021 province+cantón pair, including the new district-consistency guard and
/// the deliberately-permitted distrito-less pair (plan Decision 6).
/// </summary>
[TestFixture]
public class SupplierBranchLocationTests
{
    // Domain catalog entities are identity-generated (Id has a private setter, 0
    // in-memory). Set it via reflection so the SetLocation id-match guard has real
    // ids to check — the same technique MigrationTests uses for private setters.
    private static T WithId<T>(T entity, int id)
    {
        typeof(T).GetProperty("Id")!
            .GetSetMethod(nonPublic: true)!
            .Invoke(entity, new object?[] { id });
        return entity;
    }

    private static SupplierBranch NewBranch() =>
        Supplier.CreateDraft("3-101-1", "ACME", 1, "Sede principal",
            null, null, null, null, null, null, null).Branches.First();

    [Test]
    public void SetLocation_AllThreeValid_SetsAllRefs()
    {
        var canton = WithId(new Canton(provinceId: 1, code: "01_01", name: "San José"), 2);
        var district = WithId(new District(cantonId: 2, code: "01_01_01", name: "Carmen"), 3);
        var branch = NewBranch();

        branch.SetLocation(provinceId: 1, cantonId: 2, districtId: 3, canton, district);

        Assert.Multiple(() =>
        {
            Assert.That(branch.ProvinceId, Is.EqualTo(1));
            Assert.That(branch.CantonId, Is.EqualTo(2));
            Assert.That(branch.DistrictId, Is.EqualTo(3));
            Assert.That(branch.CantonRef, Is.SameAs(canton));
            Assert.That(branch.DistrictRef, Is.SameAs(district));
        });
    }

    [Test]
    public void SetLocation_DistrictWithoutCanton_Throws()
    {
        var district = WithId(new District(cantonId: 2, code: "01_01_01", name: "Carmen"), 3);
        var branch = NewBranch();

        // province + cantón both null (pair rule satisfied), but a distrito is set.
        Assert.That(
            () => branch.SetLocation(provinceId: null, cantonId: null, districtId: 3, canton: null, district),
            Throws.ArgumentException);
    }

    [Test]
    public void SetLocation_DistrictBelongsToDifferentCanton_Throws()
    {
        var canton = WithId(new Canton(provinceId: 1, code: "01_01", name: "San José"), 2);
        // District whose CantonId (99) does not match the submitted cantonId (2).
        var district = WithId(new District(cantonId: 99, code: "01_99_01", name: "Otro"), 3);
        var branch = NewBranch();

        Assert.That(
            () => branch.SetLocation(provinceId: 1, cantonId: 2, districtId: 3, canton, district),
            Throws.ArgumentException);
    }

    [Test]
    public void SetLocation_ProvinceAndCantonWithoutDistrict_Allowed()
    {
        var canton = WithId(new Canton(provinceId: 1, code: "01_01", name: "San José"), 2);
        var branch = NewBranch();

        branch.SetLocation(provinceId: 1, cantonId: 2, districtId: null, canton, district: null);

        Assert.Multiple(() =>
        {
            Assert.That(branch.ProvinceId, Is.EqualTo(1));
            Assert.That(branch.CantonId, Is.EqualTo(2));
            Assert.That(branch.DistrictId, Is.Null);
            Assert.That(branch.DistrictRef, Is.Null);
        });
    }

    [Test]
    public void SetLocation_AllNull_Allowed()
    {
        var branch = NewBranch();

        branch.SetLocation(provinceId: null, cantonId: null, districtId: null, canton: null, district: null);

        Assert.Multiple(() =>
        {
            Assert.That(branch.ProvinceId, Is.Null);
            Assert.That(branch.CantonId, Is.Null);
            Assert.That(branch.DistrictId, Is.Null);
        });
    }

    [Test]
    public void SetLocation_DistrictEntityIdMismatch_Throws()
    {
        var canton = WithId(new Canton(provinceId: 1, code: "01_01", name: "San José"), 2);
        var district = WithId(new District(cantonId: 2, code: "01_01_01", name: "Carmen"), 3);
        var branch = NewBranch();

        // districtId argument (4) disagrees with district.Id (3).
        Assert.That(
            () => branch.SetLocation(provinceId: 1, cantonId: 2, districtId: 4, canton, district),
            Throws.ArgumentException);
    }
}
