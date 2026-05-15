using FundingPlatform.Application.Abstractions.AiComparison;
using FundingPlatform.Application.AiComparison;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace FundingPlatform.Tests.Unit.AiComparison;

public class TokenCapGuardTests
{
    private static TokenCapGuard Build(int cap = 200_000)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AiComparison:TokenCapPerRunInput"] = cap.ToString(),
            })
            .Build();
        return new TokenCapGuard(config, NullLogger<TokenCapGuard>.Instance);
    }

    [Test]
    public void SmallPayload_PassesEnforce()
    {
        var guard = Build();
        var inputs = new[]
        {
            new TokenCapInput("Proveedor A — quote.pdf", 10_000),
            new TokenCapInput("Proveedor B — quote.pdf", 12_000),
        };
        Assert.DoesNotThrow(() => guard.Enforce(inputs, "Reviewer", bypassTokenCap: false));
    }

    [Test]
    public void OverBudget_ThrowsAndNamesLargest()
    {
        var guard = Build(cap: 5_000);
        var inputs = new[]
        {
            new TokenCapInput("Proveedor A — quote.pdf", 100_000),
            new TokenCapInput("Proveedor B — quote.pdf", 200_000),
        };
        var ex = Assert.Throws<TokenCapExceededException>(() =>
            guard.Enforce(inputs, "Reviewer", bypassTokenCap: false));
        Assert.That(ex!.OffendingInput, Does.Contain("Proveedor B"));
    }

    [Test]
    public void AdminBypass_AllowsOverBudget()
    {
        var guard = Build(cap: 5_000);
        var inputs = new[]
        {
            new TokenCapInput("Proveedor A", 50_000_000),
        };
        Assert.DoesNotThrow(() => guard.Enforce(inputs, "Admin", bypassTokenCap: true));
    }

    [Test]
    public void ReviewerBypass_StillRejected()
    {
        var guard = Build(cap: 5_000);
        var inputs = new[] { new TokenCapInput("A", 50_000_000) };
        Assert.Throws<TokenCapExceededException>(() =>
            guard.Enforce(inputs, "Reviewer", bypassTokenCap: true));
    }
}
