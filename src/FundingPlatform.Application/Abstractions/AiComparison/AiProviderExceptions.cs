namespace FundingPlatform.Application.Abstractions.AiComparison;

/// <summary>FR-I1 — HTTP 5xx, network timeout, provider 429. Show "Reintentar".</summary>
public sealed class AiProviderTransientException : Exception
{
    public AiProviderTransientException(string message) : base(message) { }
    public AiProviderTransientException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>FR-I2 — non-429 4xx, invalid API key, model deprecated.</summary>
public sealed class AiProviderHardException : Exception
{
    public string ProviderCode { get; }
    public AiProviderHardException(string providerCode, string message) : base(message)
    {
        ProviderCode = providerCode;
    }
    public AiProviderHardException(string providerCode, string message, Exception inner)
        : base(message, inner)
    {
        ProviderCode = providerCode;
    }
}

/// <summary>FR-I3 — AI response failed JSON-Schema validation. Carries first error path.</summary>
public sealed class AiSchemaInvalidException : Exception
{
    public string ValidatorPath { get; }
    public AiSchemaInvalidException(string validatorPath, string message) : base(message)
    {
        ValidatorPath = validatorPath;
    }
}

/// <summary>FR-G1 — per-app 24h rate cap; admin bypass not chosen.</summary>
public sealed class RateLimitExceededException : Exception
{
    public DateTimeOffset WindowResetsAt { get; }
    public int Remaining { get; }
    public RateLimitExceededException(int remaining, DateTimeOffset windowResetsAt)
        : base($"Per-application rate limit hit ({remaining} remaining; resets at {windowResetsAt:O}).")
    {
        Remaining = remaining;
        WindowResetsAt = windowResetsAt;
    }
}

/// <summary>FR-G2 — pre-flight token estimate exceeds cap; admin bypass not chosen.</summary>
public sealed class TokenCapExceededException : Exception
{
    public int EstimatedTokens { get; }
    public int Cap { get; }
    public string OffendingInput { get; }
    public TokenCapExceededException(int estimatedTokens, int cap, string offendingInput)
        : base($"Pre-flight token estimate {estimatedTokens} exceeds cap {cap}. Offending input: {offendingInput}.")
    {
        EstimatedTokens = estimatedTokens;
        Cap = cap;
        OffendingInput = offendingInput;
    }
}

/// <summary>Concurrent regeneration on the same item — second click is rejected.</summary>
public sealed class ConcurrentGenerationException : Exception
{
    public ConcurrentGenerationException(int applicationItemId)
        : base($"Ya hay una generación en curso para el ítem {applicationItemId}.")
    { }
}
