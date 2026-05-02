using System.Reflection;
using FundingPlatform.Domain.Entities;
using FundingPlatform.Domain.Enums;
using AppEntity = FundingPlatform.Domain.Entities.Application;

namespace FundingPlatform.Tests.Unit.Domain;

[TestFixture]
public class FundingAgreementLockdownTests
{
    [Test]
    public void IsLocked_EmptyAgreement_ReturnsFalse()
    {
        var agreement = MakeAgreement();
        Assert.That(agreement.IsLocked, Is.False);
        Assert.That(agreement.PendingUpload, Is.Null);
    }

    [Test]
    public void AcceptSignedUpload_HappyPath_LocksAgreementAndSetsPending()
    {
        var agreement = MakeAgreement();

        var upload = InvokeAccept(agreement, "applicant-1", 1, "signed.pdf", 1024, "/store/signed.pdf");

        Assert.That(agreement.IsLocked, Is.True);
        Assert.That(agreement.PendingUpload, Is.SameAs(upload));
        Assert.That(upload.Status, Is.EqualTo(SignedUploadStatus.Pending));
    }

    [Test]
    public void AcceptSignedUpload_WhenPendingExists_Throws()
    {
        var agreement = MakeAgreement();
        InvokeAccept(agreement, "applicant-1", 1, "signed.pdf", 1024, "/store/first.pdf");

        Assert.Throws<InvalidOperationException>(() =>
            InvokeAccept(agreement, "applicant-1", 1, "signed.pdf", 1024, "/store/second.pdf"));
    }

    [Test]
    public void AcceptSignedUpload_WhenVersionMismatch_Throws()
    {
        var agreement = MakeAgreement();

        Assert.Throws<InvalidOperationException>(() =>
            InvokeAccept(agreement, "applicant-1", generatedVersionAtUpload: 2, "s.pdf", 1, "/s"));
    }

    [Test]
    public void Replace_WhenLocked_Throws()
    {
        var agreement = MakeAgreement();
        InvokeAccept(agreement, "applicant-1", 1, "signed.pdf", 1024, "/store/a.pdf");

        Assert.Throws<InvalidOperationException>(() =>
            InvokeReplace(agreement, "new.pdf", 1, "/store/new.pdf", "reviewer-1"));
    }

    [Test]
    public void GeneratedVersion_StartsAtOne_IncrementsOnEachReplace()
    {
        var agreement = MakeAgreement();
        Assert.That(agreement.GeneratedVersion, Is.EqualTo(1));

        InvokeReplace(agreement, "v2.pdf", 2, "/store/v2", "reviewer-1");
        Assert.That(agreement.GeneratedVersion, Is.EqualTo(2));

        InvokeReplace(agreement, "v3.pdf", 3, "/store/v3", "reviewer-1");
        Assert.That(agreement.GeneratedVersion, Is.EqualTo(3));
    }

    [Test]
    public void ReplacePendingUpload_SupersedesOldAndCreatesNewPending()
    {
        var agreement = MakeAgreement();
        var first = InvokeAccept(agreement, "applicant-1", 1, "one.pdf", 1024, "/store/one.pdf");

        var second = InvokeReplacePending(agreement, "applicant-1", 1, "two.pdf", 2048, "/store/two.pdf");

        Assert.That(first.Status, Is.EqualTo(SignedUploadStatus.Superseded));
        Assert.That(second.Status, Is.EqualTo(SignedUploadStatus.Pending));
        Assert.That(agreement.SignedUploads, Has.Count.EqualTo(2));
        Assert.That(agreement.PendingUpload, Is.SameAs(second));
    }

    [Test]
    public void WithdrawPendingUpload_HappyPath_TransitionsToWithdrawn()
    {
        var agreement = MakeAgreement();
        var upload = InvokeAccept(agreement, "applicant-1", 1, "one.pdf", 1024, "/store/one.pdf");

        InvokeWithdraw(agreement, "applicant-1");

        Assert.That(upload.Status, Is.EqualTo(SignedUploadStatus.Withdrawn));
        Assert.That(agreement.PendingUpload, Is.Null);
        Assert.That(agreement.IsLocked, Is.True, "Any signed upload in any status locks the agreement.");
    }

    private static FundingAgreement MakeAgreement()
    {
        var ctor = typeof(FundingAgreement).GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            types: new[] { typeof(int), typeof(string), typeof(string), typeof(long), typeof(string), typeof(string) },
            modifiers: null)!;

        return (FundingAgreement)ctor.Invoke(new object[]
        {
            42, "a.pdf", "application/pdf", 1L, "/store/a.pdf", "user-1"
        });
    }

    private static SignedUpload InvokeAccept(
        FundingAgreement agreement,
        string uploaderUserId,
        int generatedVersionAtUpload,
        string fileName,
        long size,
        string blobKey)
    {
        var method = typeof(FundingAgreement).GetMethod("AcceptSignedUpload",
            BindingFlags.Instance | BindingFlags.NonPublic)!;

        try
        {
            return (SignedUpload)method.Invoke(agreement, new object[]
            {
                uploaderUserId, generatedVersionAtUpload, fileName, size, blobKey
            })!;
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            throw ex.InnerException;
        }
    }

    private static SignedUpload InvokeReplacePending(
        FundingAgreement agreement,
        string uploaderUserId,
        int generatedVersionAtUpload,
        string fileName,
        long size,
        string blobKey)
    {
        var method = typeof(FundingAgreement).GetMethod("ReplacePendingUpload",
            BindingFlags.Instance | BindingFlags.NonPublic)!;

        try
        {
            return (SignedUpload)method.Invoke(agreement, new object[]
            {
                uploaderUserId, generatedVersionAtUpload, fileName, size, blobKey
            })!;
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            throw ex.InnerException;
        }
    }

    private static void InvokeReplace(
        FundingAgreement agreement,
        string fileName,
        long size,
        string blobKey,
        string userId)
    {
        var method = typeof(FundingAgreement).GetMethod("Replace",
            BindingFlags.Instance | BindingFlags.NonPublic)!;

        try
        {
            method.Invoke(agreement, new object[]
            {
                fileName, "application/pdf", size, blobKey, userId
            });
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            throw ex.InnerException;
        }
    }

    private static void InvokeWithdraw(FundingAgreement agreement, string userId)
    {
        var method = typeof(FundingAgreement).GetMethod("WithdrawPendingUpload",
            BindingFlags.Instance | BindingFlags.NonPublic)!;

        try { method.Invoke(agreement, new object[] { userId }); }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            throw ex.InnerException;
        }
    }
}
