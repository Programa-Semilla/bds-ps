using System.Reflection;
using FundingPlatform.Domain.Entities;
using FundingPlatform.Domain.Enums;
using FundingPlatform.Domain.ValueObjects;

namespace FundingPlatform.Tests.Unit.Domain;

/// <summary>
/// Spec 023 / T007 / FR-004 — unit-level coverage of
/// <see cref="Quotation.ChangeBranch"/>:
///   1. Same-supplier branch swap updates both <see cref="Quotation.SupplierBranchId"/>
///      and the <see cref="Quotation.SupplierBranch"/> navigation.
///   2. Cross-supplier branch throws <see cref="ArgumentException"/> with the
///      es-CR message wired through to <c>ModelState</c> on FR-005.
///   3. Null branch throws <see cref="ArgumentNullException"/>.
///   4. Currency / Snapshot / ConvertedCrcAmount are NEVER mutated by a branch swap
///      (spec 023 acceptance scenario 2: "branch change with no exchange-rate
///      side effects").
/// </summary>
[TestFixture]
public class Quotation_ChangeBranchTests
{
    /// <summary>
    /// Builds a quotation with the given supplier id wired in. The Quotation
    /// constructor accepts a <c>supplierId</c> int and the same id is stamped
    /// on a fabricated <see cref="SupplierBranch"/> via reflection so the
    /// invariant under test can be exercised without standing up a full
    /// aggregate.
    /// </summary>
    private static Quotation NewQuotation(int supplierId = 1, int branchId = 10) =>
        new(supplierId: supplierId, supplierBranchId: branchId, documentId: 1, price: 100m,
            validUntil: DateOnly.FromDateTime(DateTime.UtcNow.AddYears(1)), currency: "CRC",
            deliveryLeadTime: new TimeDuration(30, DurationUnit.Days),
            warranty: new TimeDuration(12, DurationUnit.Months));

    private static SupplierBranch FabricateBranch(int branchId, int supplierId)
    {
        // SupplierBranch.ctor is internal; SupplierId / Id are private set. Use
        // reflection to construct + populate the fields under test.
        var branch = (SupplierBranch)Activator.CreateInstance(
            typeof(SupplierBranch),
            BindingFlags.NonPublic | BindingFlags.Instance,
            binder: null,
            args: null,
            culture: null)!;
        typeof(SupplierBranch).GetProperty(nameof(SupplierBranch.Id))!
            .SetValue(branch, branchId);
        typeof(SupplierBranch).GetProperty(nameof(SupplierBranch.SupplierId))!
            .SetValue(branch, supplierId);
        return branch;
    }

    [Test]
    public void ChangeBranch_WithSameSupplierBranch_UpdatesBranchAndId()
    {
        var quotation = NewQuotation(supplierId: 7, branchId: 10);
        var sameSupplierNewBranch = FabricateBranch(branchId: 11, supplierId: 7);

        quotation.ChangeBranch(sameSupplierNewBranch);

        Assert.That(quotation.SupplierBranchId, Is.EqualTo(11));
        Assert.That(quotation.SupplierBranch, Is.SameAs(sameSupplierNewBranch));
    }

    [Test]
    public void ChangeBranch_WithCrossSupplierBranch_ThrowsArgumentException()
    {
        var quotation = NewQuotation(supplierId: 7);
        var foreignBranch = FabricateBranch(branchId: 99, supplierId: 8);

        var ex = Assert.Throws<ArgumentException>(() => quotation.ChangeBranch(foreignBranch));
        Assert.That(ex!.Message, Does.StartWith("Sucursal no válida para este proveedor."));
        Assert.That(ex.ParamName, Is.EqualTo("branch"));
    }

    [Test]
    public void ChangeBranch_WithNull_ThrowsArgumentNullException()
    {
        var quotation = NewQuotation();
        Assert.Throws<ArgumentNullException>(() => quotation.ChangeBranch(null!));
    }

    [Test]
    public void ChangeBranch_DoesNotMutateCurrencyOrSnapshotOrConvertedCrcAmount()
    {
        var quotation = NewQuotation(supplierId: 7, branchId: 10);
        var beforeCurrency = quotation.Currency;
        var beforeSnapshot = quotation.Snapshot;
        var beforeConverted = quotation.ConvertedCrcAmount;
        var beforePrice = quotation.Price;

        var sibling = FabricateBranch(branchId: 12, supplierId: 7);
        quotation.ChangeBranch(sibling);

        Assert.That(quotation.Currency, Is.EqualTo(beforeCurrency));
        Assert.That(quotation.Snapshot, Is.SameAs(beforeSnapshot));
        Assert.That(quotation.ConvertedCrcAmount, Is.EqualTo(beforeConverted));
        Assert.That(quotation.Price, Is.EqualTo(beforePrice));
    }
}
