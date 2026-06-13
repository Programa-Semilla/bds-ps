using FundingPlatform.Application.Admin.Users.Batch;

namespace FundingPlatform.Tests.Unit.Batch;

public class EmailNormalizerTests
{
    [Test]
    public void SingleEmail_ReturnedUnchanged()
    {
        Assert.That(EmailNormalizer.FirstEmail("ana@example.cr"), Is.EqualTo("ana@example.cr"));
    }

    [Test]
    [TestCase("a@x.com / b@y.com")]
    [TestCase("a@x.com,b@y.com")]
    [TestCase("a@x.com ; b@y.com")]
    [TestCase("a@x.com b@y.com")]
    public void MultipleEmails_TakesFirst(string raw)
    {
        Assert.That(EmailNormalizer.FirstEmail(raw), Is.EqualTo("a@x.com"));
    }

    [Test]
    public void RealWorldMultiEmail_TakesFirst()
    {
        Assert.That(
            EmailNormalizer.FirstEmail("giancarlomaddaloni@gmail.com / giancarlo@theglutenfreelab.com"),
            Is.EqualTo("giancarlomaddaloni@gmail.com"));
    }

    [Test]
    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void BlankCell_ReturnsEmpty(string? raw)
    {
        Assert.That(EmailNormalizer.FirstEmail(raw), Is.EqualTo(string.Empty));
    }
}
