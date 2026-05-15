using FundingPlatform.Application.Abstractions.AiComparison;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace FundingPlatform.Application.AiComparison;

/// <summary>
/// Spec 020 / FR-G2 — pre-flight token-cap guard. Rough estimate based on
/// blob byte sizes (~4 bytes/token) + structured-payload character count.
/// Throws <see cref="TokenCapExceededException"/> when the estimate exceeds
/// the configured cap unless admin override is in play.
/// </summary>
public class TokenCapGuard
{
    private const int CharsPerToken = 4;

    private readonly IConfiguration _configuration;
    private readonly ILogger<TokenCapGuard> _logger;

    public TokenCapGuard(IConfiguration configuration, ILogger<TokenCapGuard> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public virtual void Enforce(
        IReadOnlyList<TokenCapInput> inputs,
        string actorRole,
        bool bypassTokenCap)
    {
        if (bypassTokenCap && string.Equals(actorRole, "Admin", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation("TokenCapGuard bypassed by admin.");
            return;
        }

        var cap = int.TryParse(_configuration["AiComparison:TokenCapPerRunInput"], out var c) ? c : 200_000;
        var totalTokens = inputs.Sum(i => (int)(i.SizeBytes / CharsPerToken));

        if (totalTokens > cap)
        {
            var biggest = inputs.OrderByDescending(i => i.SizeBytes).First();
            throw new TokenCapExceededException(
                estimatedTokens: totalTokens,
                cap: cap,
                offendingInput: biggest.Description);
        }
    }
}

public sealed record TokenCapInput(string Description, long SizeBytes);
