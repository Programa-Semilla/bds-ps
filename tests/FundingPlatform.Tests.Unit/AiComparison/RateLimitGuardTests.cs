using FundingPlatform.Application.Abstractions.AiComparison;
using FundingPlatform.Application.AiComparison;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace FundingPlatform.Tests.Unit.AiComparison;

public class RateLimitGuardTests
{
    private static RateLimitGuard Build(IRateLimitCounter counter, int cap = 10)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AiComparison:RateLimitPerApp24h"] = cap.ToString(),
            })
            .Build();
        return new RateLimitGuard(counter, config, NullLogger<RateLimitGuard>.Instance);
    }

    [Test]
    public async Task NineEvents_AllowsTenth()
    {
        var counter = Substitute.For<IRateLimitCounter>();
        counter.CountAttemptsAsync(Arg.Any<int>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(9);

        var guard = Build(counter);
        await guard.EnforceAsync(applicationId: 1, "Reviewer", bypassRateLimit: false, CancellationToken.None);
        // No throw.
    }

    [Test]
    public void TenEvents_BlocksEleventh()
    {
        var counter = Substitute.For<IRateLimitCounter>();
        counter.CountAttemptsAsync(Arg.Any<int>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(10);

        var guard = Build(counter);
        Assert.ThrowsAsync<RateLimitExceededException>(async () =>
            await guard.EnforceAsync(1, "Reviewer", bypassRateLimit: false, CancellationToken.None));
    }

    [Test]
    public async Task AdminBypass_AllowsBeyondCap()
    {
        var counter = Substitute.For<IRateLimitCounter>();
        counter.CountAttemptsAsync(Arg.Any<int>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(50);

        var guard = Build(counter);
        await guard.EnforceAsync(1, "Admin", bypassRateLimit: true, CancellationToken.None);
        // No throw — admin override.
    }

    [Test]
    public void ReviewerBypass_StillRejected()
    {
        var counter = Substitute.For<IRateLimitCounter>();
        counter.CountAttemptsAsync(Arg.Any<int>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(10);

        var guard = Build(counter);
        Assert.ThrowsAsync<RateLimitExceededException>(async () =>
            await guard.EnforceAsync(1, "Reviewer", bypassRateLimit: true, CancellationToken.None));
    }
}
