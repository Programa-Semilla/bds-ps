using FundingPlatform.Domain.Entities;
using FundingPlatform.Domain.Interfaces;
using NSubstitute;

namespace FundingPlatform.Tests.Unit.Domain;

/// <summary>
/// Spec 021 / FR-028 / SC-009 / R-3 — single-use token semantics layered on top
/// of Identity's <c>DataProtectorTokenProvider</c>. The domain enforces the
/// single-use + expiry invariants in <see cref="PasswordResetToken.Consume"/>.
/// </summary>
[TestFixture]
public class PasswordResetTokenTests
{
    private static IStageExpiryClock ClockAt(DateTimeOffset now)
    {
        var clock = Substitute.For<IStageExpiryClock>();
        clock.UtcNow.Returns(now);
        return clock;
    }

    [Test]
    public void Issue_StampsExpiresAtAsNowPlusTtl()
    {
        var now = new DateTimeOffset(2026, 5, 13, 10, 0, 0, TimeSpan.Zero);
        var ttl = TimeSpan.FromMinutes(60);
        var hash = new byte[] { 1, 2, 3, 4 };

        var token = PasswordResetToken.Issue("user-1", hash, now, ttl);

        Assert.That(token.UserId, Is.EqualTo("user-1"));
        Assert.That(token.IssuedAt, Is.EqualTo(now));
        Assert.That(token.ExpiresAt, Is.EqualTo(now.AddMinutes(60)));
        Assert.That(token.ConsumedAt, Is.Null);
        Assert.That(token.IsConsumed, Is.False);
        Assert.That(token.TokenHash, Is.EqualTo(hash));
    }

    [Test]
    public void Issue_DefaultsTo60MinuteLifetime()
    {
        var now = new DateTimeOffset(2026, 5, 13, 10, 0, 0, TimeSpan.Zero);

        var token = PasswordResetToken.Issue("user-1", new byte[] { 1 }, now);

        Assert.That(token.ExpiresAt, Is.EqualTo(now.Add(PasswordResetToken.DefaultLifetime)));
        Assert.That(PasswordResetToken.DefaultLifetime, Is.EqualTo(TimeSpan.FromMinutes(60)));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void Issue_RejectsBlankUserId(string? raw)
    {
        var now = DateTimeOffset.UtcNow;

        Assert.Throws<ArgumentException>(
            () => PasswordResetToken.Issue(raw!, new byte[] { 1 }, now));
    }

    [Test]
    public void Issue_RejectsEmptyHash()
    {
        var now = DateTimeOffset.UtcNow;

        Assert.Throws<ArgumentException>(
            () => PasswordResetToken.Issue("user-1", Array.Empty<byte>(), now));
    }

    [Test]
    public void Issue_RejectsNullHash()
    {
        var now = DateTimeOffset.UtcNow;

        Assert.Throws<ArgumentNullException>(
            () => PasswordResetToken.Issue("user-1", null!, now));
    }

    [TestCase(0)]
    [TestCase(-1)]
    public void Issue_RejectsNonPositiveTtl(int ttlSeconds)
    {
        var now = DateTimeOffset.UtcNow;

        Assert.Throws<ArgumentException>(
            () => PasswordResetToken.Issue(
                "user-1", new byte[] { 1 }, now, TimeSpan.FromSeconds(ttlSeconds)));
    }

    [Test]
    public void Consume_FirstCall_SetsConsumedAt()
    {
        var issuedAt = new DateTimeOffset(2026, 5, 13, 10, 0, 0, TimeSpan.Zero);
        var token = PasswordResetToken.Issue(
            "user-1", new byte[] { 1 }, issuedAt, TimeSpan.FromMinutes(60));
        var consumeAt = issuedAt.AddMinutes(5);

        token.Consume(ClockAt(consumeAt));

        Assert.That(token.IsConsumed, Is.True);
        Assert.That(token.ConsumedAt, Is.EqualTo(consumeAt));
    }

    [Test]
    public void Consume_SecondCall_Throws()
    {
        var issuedAt = new DateTimeOffset(2026, 5, 13, 10, 0, 0, TimeSpan.Zero);
        var token = PasswordResetToken.Issue(
            "user-1", new byte[] { 1 }, issuedAt, TimeSpan.FromMinutes(60));
        token.Consume(ClockAt(issuedAt.AddMinutes(5)));

        Assert.Throws<InvalidOperationException>(
            () => token.Consume(ClockAt(issuedAt.AddMinutes(10))));
    }

    [Test]
    public void Consume_AfterExpiry_Throws()
    {
        var issuedAt = new DateTimeOffset(2026, 5, 13, 10, 0, 0, TimeSpan.Zero);
        var token = PasswordResetToken.Issue(
            "user-1", new byte[] { 1 }, issuedAt, TimeSpan.FromMinutes(60));
        var consumeAt = issuedAt.AddMinutes(61); // 1 minute past expiry

        Assert.Throws<InvalidOperationException>(() => token.Consume(ClockAt(consumeAt)));
        Assert.That(token.IsConsumed, Is.False);
    }

    /// <summary>
    /// Edge case: now == ExpiresAt. Implementation uses <c>now &gt;= ExpiresAt</c>,
    /// so the boundary is rejected (expiry is exclusive). Test the implemented behavior.
    /// </summary>
    [Test]
    public void Consume_AtExactExpiryInstant_Throws()
    {
        var issuedAt = new DateTimeOffset(2026, 5, 13, 10, 0, 0, TimeSpan.Zero);
        var token = PasswordResetToken.Issue(
            "user-1", new byte[] { 1 }, issuedAt, TimeSpan.FromMinutes(60));
        var consumeAt = token.ExpiresAt; // exactly at expiry

        Assert.Throws<InvalidOperationException>(() => token.Consume(ClockAt(consumeAt)));
        Assert.That(token.IsConsumed, Is.False);
    }
}
