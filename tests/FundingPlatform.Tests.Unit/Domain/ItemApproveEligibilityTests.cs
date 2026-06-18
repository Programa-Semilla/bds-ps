using FundingPlatform.Domain.Entities;
using FundingPlatform.Domain.Enums;
using FundingPlatform.Domain.Exceptions;
using FundingPlatform.Domain.ValueObjects;

namespace FundingPlatform.Tests.Unit.Domain;

/// <summary>
/// Spec 039 / FR-019 / US3 — the CCSS sin inscripción progression gate on
/// <see cref="Item.Approve"/>: an item cannot be approved with a sin-inscripción
/// provider selected; null CCSS (sin revisar) does not block; eligible providers
/// approve normally.
/// </summary>
[TestFixture]
public class ItemApproveEligibilityTests
{
    [Test]
    public void Approve_WithCcssSinInscripcionProvider_Throws()
    {
        var (item, supplierId) = BuildItemWithSupplier(CcssStatus.SinInscripcion);

        var ex = Assert.Throws<SupplierIneligibleException>(() => item.Approve(supplierId, "ok"));
        Assert.That(ex!.SupplierName, Is.Not.Empty);
        Assert.That(item.ReviewStatus, Is.EqualTo(ItemReviewStatus.Pending), "no approval persisted");
        Assert.That(item.SelectedSupplierId, Is.Null);
    }

    [Test]
    public void Approve_WithAlDiaProvider_Succeeds()
    {
        var (item, supplierId) = BuildItemWithSupplier(CcssStatus.AlDia);

        item.Approve(supplierId, "ok");

        Assert.That(item.ReviewStatus, Is.EqualTo(ItemReviewStatus.Approved));
        Assert.That(item.SelectedSupplierId, Is.EqualTo(supplierId));
    }

    [Test]
    public void Approve_WithNullCcss_DoesNotBlock()
    {
        var (item, supplierId) = BuildItemWithSupplier(null);

        item.Approve(supplierId, "ok");

        Assert.That(item.ReviewStatus, Is.EqualTo(ItemReviewStatus.Approved));
    }

    [TestCase(CcssStatus.EstadoMoroso)]
    [TestCase(CcssStatus.CobroJudicial)]
    public void Approve_WithOtherCcssStatuses_DoesNotBlock(CcssStatus status)
    {
        var (item, supplierId) = BuildItemWithSupplier(status);

        item.Approve(supplierId, "ok");

        Assert.That(item.ReviewStatus, Is.EqualTo(ItemReviewStatus.Approved));
    }

    private static (Item item, int supplierId) BuildItemWithSupplier(CcssStatus? ccss)
    {
        const int supplierId = 7;
        var supplier = Supplier.CreateDraft(
            legalId: "LEG-7", name: "Proveedor Siete", createdByApplicantId: 1,
            firstBranchName: "Sede principal",
            firstBranchContactName: null, firstBranchEmail: null, firstBranchPhone: null,
            firstBranchAddressLine: null, firstBranchProvince: null,
            firstBranchShippingDetails: null, firstBranchWarrantyInfo: null);
        supplier.ApplyRegulatoryEdit(
            hacienda: null, ccss: ccss, sicop: null, isPmeOrPyme: false,
            hasWarning: false, warningNote: null, actorUserId: "test-actor", nowUtc: DateTime.UtcNow);
        typeof(Supplier).GetProperty("Id")!.SetValue(supplier, supplierId);

        var branch = supplier.Branches.First();
        typeof(SupplierBranch).GetProperty("Id")!.SetValue(branch, 70);
        typeof(SupplierBranch).GetProperty("SupplierId")!.SetValue(branch, supplierId);

        var quotation = new Quotation(
            supplierId: supplierId, supplierBranchId: 70, documentId: 1, price: 100m,
            validUntil: DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(3)), currency: "CRC",
            deliveryLeadTime: new TimeDuration(30, DurationUnit.Days),
            warranty: new TimeDuration(12, DurationUnit.Months));
        // Set the Supplier nav the gate reads (EF loads it in production).
        typeof(Quotation).GetProperty("Supplier")!.SetValue(quotation, supplier);

        var item = new Item("Producto", categoryId: 1);
        item.AttachQuotation(supplier, branch, quotation);

        return (item, supplierId);
    }
}
