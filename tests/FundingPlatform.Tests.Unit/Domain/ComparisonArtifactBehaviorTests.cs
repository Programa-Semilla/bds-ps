using FundingPlatform.Domain.Entities;

namespace FundingPlatform.Tests.Unit.Domain;

public class ComparisonArtifactBehaviorTests
{
    private const string Hash64A = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string Hash64B = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

    private static ComparisonArtifact Build(string hash = Hash64A, string schema = "v1", string prompt = "2026-05-11")
        => ComparisonArtifact.Create(
            applicationItemId: 1,
            jsonContent: "{ \"schemaVersion\": \"v1\", \"items\": [] }",
            inputHash: hash,
            promptVersion: prompt,
            schemaVersion: schema,
            aiModel: "claude-opus-4-7",
            generatedByUserId: "user-1",
            tokenIn: 100, tokenOut: 50, latencyMs: 1234,
            generatedAt: DateTimeOffset.UtcNow);

    [Test]
    public void Create_RejectsBadHashShape()
    {
        Assert.Throws<ArgumentException>(() => ComparisonArtifact.Create(
            1, "{}", "not-a-hash", "p", "v1", "model", "user", 0, 0, 0, DateTimeOffset.UtcNow));
    }

    [Test]
    public void Create_RejectsNegativeTokens()
    {
        Assert.Throws<ArgumentException>(() => ComparisonArtifact.Create(
            1, "{}", Hash64A, "p", "v1", "model", "user", -1, 0, 0, DateTimeOffset.UtcNow));
    }

    [Test]
    public void IsStaleAgainst_MatchingHash_ReportsFresh()
    {
        var artifact = Build();
        var result = artifact.IsStaleAgainst(Hash64A, "2026-05-11", "v1");
        Assert.That(result.IsFresh, Is.True);
        Assert.That(result.ChangedInputs, Is.Empty);
    }

    [Test]
    public void IsStaleAgainst_DifferentHash_AndSchemaBump_EnumeratesSchemaBumped()
    {
        var artifact = Build(schema: "v1");
        var result = artifact.IsStaleAgainst(Hash64B, "2026-05-11", "v2");
        Assert.That(result.IsFresh, Is.False);
        Assert.That(result.ChangedInputs, Contains.Item(ChangedInput.SchemaBumped));
    }

    [Test]
    public void IsStaleAgainst_DifferentHashSamePromptSchema_FallsBackToLineEdited()
    {
        var artifact = Build();
        var result = artifact.IsStaleAgainst(Hash64B, "2026-05-11", "v1");
        Assert.That(result.IsFresh, Is.False);
        Assert.That(result.ChangedInputs, Contains.Item(ChangedInput.LineEdited));
    }

    [Test]
    public void ReplaceWith_RejectsBadHash()
    {
        var artifact = Build();
        Assert.Throws<ArgumentException>(() => artifact.ReplaceWith(
            "{}", "bad-hash", "p", "v1", "m", "u", 0, 0, 0, DateTimeOffset.UtcNow));
    }

    [Test]
    public void ReplaceWith_UpdatesFields()
    {
        var artifact = Build();
        var newAt = DateTimeOffset.UtcNow.AddMinutes(1);
        artifact.ReplaceWith(
            "{ \"new\": true }", Hash64B, "2026-06-01", "v2", "claude-opus-4-7",
            "user-2", 200, 100, 5000, newAt);

        Assert.That(artifact.InputHash, Is.EqualTo(Hash64B));
        Assert.That(artifact.SchemaVersion, Is.EqualTo("v2"));
        Assert.That(artifact.PromptVersion, Is.EqualTo("2026-06-01"));
        Assert.That(artifact.TokenCostInput, Is.EqualTo(200));
        Assert.That(artifact.GeneratedAt, Is.EqualTo(newAt));
    }
}
