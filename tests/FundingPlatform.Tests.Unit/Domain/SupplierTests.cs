using FundingPlatform.Domain.Entities;
using FundingPlatform.Domain.Enums;

namespace FundingPlatform.Tests.Unit.Domain;

[TestFixture]
public class SupplierTests
{
    [Test]
    public void CreateDraft_SetsDraftStatusAndAllFlagsFalse()
    {
        var s = MakeDraft();

        Assert.That(s.VerificationStatus, Is.EqualTo(SupplierVerificationStatus.Draft));
        Assert.That(s.HasElectronicInvoice, Is.False);
        Assert.That(s.IsCompliantCCSS, Is.False);
        Assert.That(s.IsCompliantHacienda, Is.False);
        Assert.That(s.IsCompliantSICOP, Is.False);
        Assert.That(s.CreatedByApplicantId, Is.EqualTo(42));
        Assert.That(s.Branches.Count, Is.EqualTo(1));
        Assert.That(s.Branches.First().IsDefault, Is.True);
        Assert.That(s.Branches.First().BranchName, Is.EqualTo("Sede principal"));
    }

    [Test]
    public void CreateDraft_NormalizesLegalId()
    {
        var s = Supplier.CreateDraft("  3-101-123456 ", "Test", 1,
            "Sede principal", null, null, null, null, null, null, null);
        Assert.That(s.LegalId, Is.EqualTo("3-101-123456"));
    }

    [Test]
    public void SubmitForReview_FromDraft_FlipsToPendingReview()
    {
        var s = MakeDraft();
        s.SubmitForReview();
        Assert.That(s.VerificationStatus, Is.EqualTo(SupplierVerificationStatus.PendingReview));
    }

    [Test]
    public void SubmitForReview_FromVerified_NoOp()
    {
        var s = MakeDraft();
        s.SubmitForReview();
        s.Verify("admin-1");
        var statusBefore = s.VerificationStatus;
        s.SubmitForReview(); // idempotent
        Assert.That(s.VerificationStatus, Is.EqualTo(statusBefore));
    }

    [Test]
    public void Verify_FromDraft_Throws()
    {
        var s = MakeDraft();
        Assert.Throws<InvalidOperationException>(() => s.Verify("admin-1"));
    }

    [Test]
    public void Verify_FromPendingReview_Succeeds()
    {
        var s = MakeDraft();
        s.SubmitForReview();
        s.Verify("admin-1");
        Assert.That(s.VerificationStatus, Is.EqualTo(SupplierVerificationStatus.Verified));
        Assert.That(s.VerifiedByUserId, Is.EqualTo("admin-1"));
        Assert.That(s.VerifiedAt, Is.Not.Null);
    }

    [Test]
    public void Verify_FromRejected_ClearsRejectionReason()
    {
        var s = MakeDraft();
        s.SubmitForReview();
        s.Reject("admin-1", "duplicado");
        Assert.That(s.RejectionReason, Is.EqualTo("duplicado"));
        s.Verify("admin-2");
        Assert.That(s.RejectionReason, Is.Null);
        Assert.That(s.VerifiedByUserId, Is.EqualTo("admin-2"));
    }

    [Test]
    public void Reject_RequiresNonEmptyReason()
    {
        var s = MakeDraft();
        s.SubmitForReview();
        Assert.Throws<ArgumentException>(() => s.Reject("admin-1", ""));
        Assert.Throws<ArgumentException>(() => s.Reject("admin-1", "   "));
    }

    [Test]
    public void Reject_FromDraft_Throws()
    {
        var s = MakeDraft();
        Assert.Throws<InvalidOperationException>(() => s.Reject("admin-1", "x"));
    }

    [Test]
    public void RenameByApplicant_OnDraft_Succeeds()
    {
        var s = MakeDraft();
        s.RenameByApplicant("Nuevo nombre");
        Assert.That(s.Name, Is.EqualTo("Nuevo nombre"));
    }

    [Test]
    public void RenameByApplicant_OnVerified_Throws()
    {
        var s = MakeDraft();
        s.SubmitForReview();
        s.Verify("admin-1");
        Assert.Throws<InvalidOperationException>(() => s.RenameByApplicant("X"));
    }

    [Test]
    public void AddBranch_RejectsSecondDefault()
    {
        var s = MakeDraft();
        Assert.Throws<InvalidOperationException>(() =>
            s.AddBranch("Sede 2", null, null, null, null, null, null, null, 42, isDefault: true));
    }

    [Test]
    public void AddBranch_NonDefaultSucceeds()
    {
        var s = MakeDraft();
        var branch = s.AddBranch("Sede 2", "Pedro", "p@x.com", null, null, null, null, null, 42);
        Assert.That(branch.IsDefault, Is.False);
        Assert.That(s.Branches.Count, Is.EqualTo(2));
    }

    [Test]
    public void EditByAdmin_TogglesAllAdminFlags()
    {
        var s = MakeDraft();
        s.EditByAdmin("X", true, true, true, true);
        Assert.That(s.HasElectronicInvoice, Is.True);
        Assert.That(s.IsCompliantCCSS, Is.True);
        Assert.That(s.IsCompliantHacienda, Is.True);
        Assert.That(s.IsCompliantSICOP, Is.True);
    }

    private static Supplier MakeDraft() =>
        Supplier.CreateDraft(
            legalId: "3-101-123456",
            name: "ACME S.A.",
            createdByApplicantId: 42,
            firstBranchName: "Sede principal",
            firstBranchContactName: null,
            firstBranchEmail: null,
            firstBranchPhone: null,
            firstBranchAddressLine: null,
            firstBranchProvince: null,
            firstBranchShippingDetails: null,
            firstBranchWarrantyInfo: null);
}
