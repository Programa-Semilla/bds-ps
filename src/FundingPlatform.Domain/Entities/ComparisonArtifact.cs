using System.Text.RegularExpressions;

namespace FundingPlatform.Domain.Entities;

/// <summary>
/// Spec 020 / FR-D1..FR-D4 — cached AI quote comparison artifact for one
/// ApplicationItem. One row per item (replaced in place on regeneration; no
/// history table).
///
/// Principle II — rich domain model: invariants enforced in the factory and
/// <see cref="ReplaceWith"/>; private setters; no anemic data carrier.
/// </summary>
public class ComparisonArtifact
{
    private static readonly Regex HashShape = new("^[a-f0-9]{64}$", RegexOptions.Compiled);

    public int ApplicationItemId { get; private set; }
    public string JsonContent { get; private set; } = string.Empty;
    public string InputHash { get; private set; } = string.Empty;
    public string PromptVersion { get; private set; } = string.Empty;
    public string SchemaVersion { get; private set; } = string.Empty;
    public string AiModel { get; private set; } = string.Empty;
    public DateTimeOffset GeneratedAt { get; private set; }
    public string GeneratedByUserId { get; private set; } = string.Empty;
    public int TokenCostInput { get; private set; }
    public int TokenCostOutput { get; private set; }
    public int LatencyMs { get; private set; }

    private ComparisonArtifact() { }

    private ComparisonArtifact(
        int applicationItemId,
        string jsonContent,
        string inputHash,
        string promptVersion,
        string schemaVersion,
        string aiModel,
        DateTimeOffset generatedAt,
        string generatedByUserId,
        int tokenIn,
        int tokenOut,
        int latencyMs)
    {
        ApplicationItemId = applicationItemId;
        JsonContent = jsonContent;
        InputHash = inputHash;
        PromptVersion = promptVersion;
        SchemaVersion = schemaVersion;
        AiModel = aiModel;
        GeneratedAt = generatedAt;
        GeneratedByUserId = generatedByUserId;
        TokenCostInput = tokenIn;
        TokenCostOutput = tokenOut;
        LatencyMs = latencyMs;
    }

    /// <summary>
    /// Factory enforcing invariants: non-empty fields, 64-hex hash, non-negative
    /// costs/latency. Throws <see cref="ArgumentException"/> on violation.
    /// </summary>
    public static ComparisonArtifact Create(
        int applicationItemId,
        string jsonContent,
        string inputHash,
        string promptVersion,
        string schemaVersion,
        string aiModel,
        string generatedByUserId,
        int tokenIn,
        int tokenOut,
        int latencyMs,
        DateTimeOffset generatedAt)
    {
        if (applicationItemId <= 0)
            throw new ArgumentException("ApplicationItemId must be positive.", nameof(applicationItemId));
        Validate(jsonContent, inputHash, promptVersion, schemaVersion, aiModel, generatedByUserId, tokenIn, tokenOut, latencyMs);

        return new ComparisonArtifact(
            applicationItemId, jsonContent, inputHash, promptVersion, schemaVersion,
            aiModel, generatedAt, generatedByUserId, tokenIn, tokenOut, latencyMs);
    }

    /// <summary>
    /// FR-D4 — atomic in-place replacement on successful regeneration. Validates
    /// the new state against the same invariants as construction so a malformed
    /// caller can never overwrite a healthy row with garbage.
    /// </summary>
    public void ReplaceWith(
        string jsonContent,
        string inputHash,
        string promptVersion,
        string schemaVersion,
        string aiModel,
        string generatedByUserId,
        int tokenIn,
        int tokenOut,
        int latencyMs,
        DateTimeOffset generatedAt)
    {
        Validate(jsonContent, inputHash, promptVersion, schemaVersion, aiModel, generatedByUserId, tokenIn, tokenOut, latencyMs);

        JsonContent = jsonContent;
        InputHash = inputHash;
        PromptVersion = promptVersion;
        SchemaVersion = schemaVersion;
        AiModel = aiModel;
        GeneratedByUserId = generatedByUserId;
        TokenCostInput = tokenIn;
        TokenCostOutput = tokenOut;
        LatencyMs = latencyMs;
        GeneratedAt = generatedAt;
    }

    /// <summary>
    /// FR-D3 — compare this artifact's persisted hash + prompt/schema versions
    /// against the live state. Returns <see cref="FreshnessResult.Fresh"/> on a
    /// match; otherwise enumerates whichever coarse-grained dimension changed
    /// (schema, prompt, anything-else lumped as the catch-all
    /// <see cref="ChangedInput.LineEdited"/>). Finer-grained diff (file
    /// added/removed, supplier added/removed, snapshot changed) is computed by
    /// the Application-layer freshness analyzer using the full
    /// <c>InputDescriptor</c>; this method preserves Domain purity by relying
    /// only on the inputs it can see.
    /// </summary>
    public FreshnessResult IsStaleAgainst(
        string liveInputHash,
        string livePromptVersion,
        string liveSchemaVersion,
        IReadOnlyList<ChangedInput>? extraChangedInputs = null)
    {
        if (string.IsNullOrWhiteSpace(liveInputHash))
            throw new ArgumentException("Live input hash is required.", nameof(liveInputHash));

        if (string.Equals(liveInputHash, InputHash, StringComparison.Ordinal))
            return FreshnessResult.Fresh;

        var changed = new List<ChangedInput>();
        if (!string.Equals(liveSchemaVersion, SchemaVersion, StringComparison.Ordinal))
            changed.Add(ChangedInput.SchemaBumped);
        if (!string.Equals(livePromptVersion, PromptVersion, StringComparison.Ordinal))
            changed.Add(ChangedInput.PromptVersionBumped);

        if (extraChangedInputs is not null)
        {
            foreach (var ci in extraChangedInputs)
                if (!changed.Contains(ci)) changed.Add(ci);
        }

        // Fallback so the UI always renders something meaningful when only
        // the hash changed and the analyzer didn't supply a specific dimension.
        if (changed.Count == 0)
            changed.Add(ChangedInput.LineEdited);

        return new FreshnessResult(false, changed);
    }

    private static void Validate(
        string jsonContent,
        string inputHash,
        string promptVersion,
        string schemaVersion,
        string aiModel,
        string generatedByUserId,
        int tokenIn,
        int tokenOut,
        int latencyMs)
    {
        if (string.IsNullOrWhiteSpace(jsonContent))
            throw new ArgumentException("JsonContent is required.", nameof(jsonContent));
        if (string.IsNullOrWhiteSpace(inputHash) || !HashShape.IsMatch(inputHash))
            throw new ArgumentException("InputHash must be 64 lowercase hex characters.", nameof(inputHash));
        if (string.IsNullOrWhiteSpace(promptVersion))
            throw new ArgumentException("PromptVersion is required.", nameof(promptVersion));
        if (string.IsNullOrWhiteSpace(schemaVersion))
            throw new ArgumentException("SchemaVersion is required.", nameof(schemaVersion));
        if (string.IsNullOrWhiteSpace(aiModel))
            throw new ArgumentException("AiModel is required.", nameof(aiModel));
        if (string.IsNullOrWhiteSpace(generatedByUserId))
            throw new ArgumentException("GeneratedByUserId is required.", nameof(generatedByUserId));
        if (tokenIn < 0)
            throw new ArgumentException("TokenCostInput must be non-negative.", nameof(tokenIn));
        if (tokenOut < 0)
            throw new ArgumentException("TokenCostOutput must be non-negative.", nameof(tokenOut));
        if (latencyMs < 0)
            throw new ArgumentException("LatencyMs must be non-negative.", nameof(latencyMs));
    }
}
