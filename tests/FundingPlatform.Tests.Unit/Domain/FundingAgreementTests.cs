using System.Reflection;
using FundingPlatform.Domain.Entities;

namespace FundingPlatform.Tests.Unit.Domain;

[TestFixture]
public class FundingAgreementTests
{
    [Test]
    public void Constructor_IsInternalAndNotPubliclyInvokable()
    {
        var ctor = typeof(FundingAgreement).GetConstructors(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Single(c => c.GetParameters().Length > 0);

        Assert.That(ctor.IsAssembly, Is.True,
            "FundingAgreement's value-bearing constructor must be internal so construction is gated through Application.");
    }

    [Test]
    public void Create_WithNonPdfContentType_Throws()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            Invoke(contentType: "application/octet-stream"));

        Assert.That(ex!.Message, Does.Contain("application/pdf"));
    }

    [Test]
    public void Create_WithZeroSize_Throws()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Invoke(size: 0));

        Assert.That(ex!.Message, Does.Contain("greater than zero"));
    }

    [Test]
    public void Create_WithNegativeSize_Throws()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Invoke(size: -1));

        Assert.That(ex!.Message, Does.Contain("greater than zero"));
    }

    [Test]
    public void Create_WithEmptyFileName_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => Invoke(fileName: ""));
    }

    [Test]
    public void Create_WithEmptyBlobKey_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => Invoke(blobKey: ""));
    }

    [Test]
    public void Create_WithEmptyUserId_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => Invoke(userId: ""));
    }

    [Test]
    public void Create_WithValidValues_PersistsFieldsAndStampsTimestamp()
    {
        var before = DateTime.UtcNow.AddSeconds(-1);
        var agreement = Invoke();
        var after = DateTime.UtcNow.AddSeconds(1);

        Assert.That(agreement.ApplicationId, Is.EqualTo(42));
        Assert.That(agreement.FileName, Is.EqualTo("agreement.pdf"));
        Assert.That(agreement.ContentType, Is.EqualTo("application/pdf"));
        Assert.That(agreement.Size, Is.EqualTo(123L));
        Assert.That(agreement.BlobKey, Is.EqualTo("/storage/agreement.pdf"));
        Assert.That(agreement.GeneratedByUserId, Is.EqualTo("user-1"));
        Assert.That(agreement.GeneratedAtUtc, Is.GreaterThanOrEqualTo(before).And.LessThanOrEqualTo(after));
    }

    private static FundingAgreement Invoke(
        int applicationId = 42,
        string fileName = "agreement.pdf",
        string contentType = "application/pdf",
        long size = 123,
        string blobKey = "/storage/agreement.pdf",
        string userId = "user-1")
    {
        var ctor = typeof(FundingAgreement).GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            types: new[] { typeof(int), typeof(string), typeof(string), typeof(long), typeof(string), typeof(string) },
            modifiers: null)!;

        try
        {
            return (FundingAgreement)ctor.Invoke(new object[]
            {
                applicationId, fileName, contentType, size, blobKey, userId
            });
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            throw ex.InnerException;
        }
    }
}
