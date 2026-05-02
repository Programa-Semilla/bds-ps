using System.Reflection;
using FundingPlatform.Domain.Entities;
using FundingPlatform.Domain.Enums;

namespace FundingPlatform.Tests.Unit.Domain;

[TestFixture]
public class SignedUploadTests
{
    [Test]
    public void Construction_SetsPending()
    {
        var upload = Invoke();

        Assert.That(upload.Status, Is.EqualTo(SignedUploadStatus.Pending));
        Assert.That(upload.UploaderUserId, Is.EqualTo("applicant-1"));
        Assert.That(upload.Size, Is.EqualTo(1024L));
        Assert.That(upload.ReviewDecision, Is.Null);
        Assert.That(upload.UploadedAtUtc, Is.LessThanOrEqualTo(DateTime.UtcNow.AddSeconds(1)));
    }

    [Test]
    public void MarkSuperseded_FromPending_Succeeds()
    {
        var upload = Invoke();

        InvokeTransition(upload, "MarkSuperseded");

        Assert.That(upload.Status, Is.EqualTo(SignedUploadStatus.Superseded));
    }

    [Test]
    public void MarkWithdrawn_FromPending_Succeeds()
    {
        var upload = Invoke();

        InvokeTransition(upload, "MarkWithdrawn");

        Assert.That(upload.Status, Is.EqualTo(SignedUploadStatus.Withdrawn));
    }

    [Test]
    public void Approve_FromPending_PopulatesDecision()
    {
        var upload = Invoke();

        var decision = InvokeApprove(upload, "reviewer-1", "looks good");

        Assert.That(upload.Status, Is.EqualTo(SignedUploadStatus.Approved));
        Assert.That(upload.ReviewDecision, Is.Not.Null);
        Assert.That(upload.ReviewDecision!.Outcome, Is.EqualTo(SigningDecisionOutcome.Approved));
        Assert.That(upload.ReviewDecision.Comment, Is.EqualTo("looks good"));
        Assert.That(decision, Is.SameAs(upload.ReviewDecision));
    }

    [Test]
    public void Reject_FromPending_PopulatesDecisionWithComment()
    {
        var upload = Invoke();

        var decision = InvokeReject(upload, "reviewer-1", "signature illegible");

        Assert.That(upload.Status, Is.EqualTo(SignedUploadStatus.Rejected));
        Assert.That(upload.ReviewDecision!.Outcome, Is.EqualTo(SigningDecisionOutcome.Rejected));
        Assert.That(upload.ReviewDecision.Comment, Is.EqualTo("signature illegible"));
        Assert.That(decision, Is.SameAs(upload.ReviewDecision));
    }

    [Test]
    public void Transition_FromTerminalStatus_Throws()
    {
        var upload = Invoke();
        InvokeApprove(upload, "reviewer-1", null);

        Assert.Throws<InvalidOperationException>(() => InvokeTransition(upload, "MarkSuperseded"));
        Assert.Throws<InvalidOperationException>(() => InvokeTransition(upload, "MarkWithdrawn"));
        Assert.Throws<InvalidOperationException>(() => InvokeApprove(upload, "reviewer-1", null));
        Assert.Throws<InvalidOperationException>(() => InvokeReject(upload, "reviewer-1", "x"));
    }

    [Test]
    public void Construction_WithEmptyUploaderId_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => Invoke(uploaderUserId: ""));
    }

    [Test]
    public void Construction_WithZeroSize_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => Invoke(size: 0));
    }

    [Test]
    public void Construction_WithEmptyBlobKey_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => Invoke(blobKey: ""));
    }

    private static SignedUpload Invoke(
        int fundingAgreementId = 1,
        string uploaderUserId = "applicant-1",
        int generatedVersionAtUpload = 1,
        string fileName = "signed.pdf",
        long size = 1024,
        string blobKey = "/storage/signed.pdf")
    {
        var ctor = typeof(SignedUpload).GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            types: new[] { typeof(int), typeof(string), typeof(int), typeof(string), typeof(long), typeof(string) },
            modifiers: null)!;

        try
        {
            return (SignedUpload)ctor.Invoke(new object[]
            {
                fundingAgreementId, uploaderUserId, generatedVersionAtUpload, fileName, size, blobKey
            });
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            throw ex.InnerException;
        }
    }

    private static void InvokeTransition(SignedUpload upload, string methodName)
    {
        var method = typeof(SignedUpload).GetMethod(methodName,
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!;
        try { method.Invoke(upload, Array.Empty<object>()); }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            throw ex.InnerException;
        }
    }

    private static SigningReviewDecision InvokeApprove(SignedUpload upload, string reviewerUserId, string? comment)
    {
        var method = typeof(SignedUpload).GetMethod("Approve",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        try { return (SigningReviewDecision)method.Invoke(upload, new object?[] { reviewerUserId, comment })!; }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            throw ex.InnerException;
        }
    }

    private static SigningReviewDecision InvokeReject(SignedUpload upload, string reviewerUserId, string comment)
    {
        var method = typeof(SignedUpload).GetMethod("Reject",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        try { return (SigningReviewDecision)method.Invoke(upload, new object?[] { reviewerUserId, comment })!; }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            throw ex.InnerException;
        }
    }
}
