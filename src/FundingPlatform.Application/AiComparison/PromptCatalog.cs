using Microsoft.Extensions.Configuration;

namespace FundingPlatform.Application.AiComparison;

/// <summary>
/// Spec 020 / NFR-M2 — prompts + schemas are checked-in source-tree artifacts.
/// This loader resolves them at startup from a configurable root (defaults to
/// "./prompts" and "./schemas" relative to the app working directory) so the
/// runtime references match the spec-time files.
/// </summary>
public class PromptCatalog
{
    public string ExtractPrompt { get; }
    public string ComparePrompt { get; }
    public string ExtractSchema { get; }
    public string CompareSchema { get; }
    public string PromptVersion { get; }
    public string SchemaVersion { get; }

    public PromptCatalog(IConfiguration configuration)
    {
        PromptVersion = configuration["AiComparison:PromptVersion"] ?? "2026-05-11";
        SchemaVersion = configuration["AiComparison:SchemaVersion"] ?? "v1";

        var promptsRoot = configuration["AiComparison:PromptsRoot"]
            ?? Path.Combine(AppContext.BaseDirectory, "prompts");
        var schemasRoot = configuration["AiComparison:SchemasRoot"]
            ?? Path.Combine(AppContext.BaseDirectory, "schemas");

        ExtractPrompt = ReadOrFallback(Path.Combine(promptsRoot, "extract.v1.md"), "prompts/extract.v1.md");
        ComparePrompt = ReadOrFallback(Path.Combine(promptsRoot, "compare.v1.md"), "prompts/compare.v1.md");
        ExtractSchema = ReadOrFallback(
            Path.Combine(schemasRoot, "ExtractedSupplierOffering.v1.schema.json"),
            "schemas/ExtractedSupplierOffering.v1.schema.json");
        CompareSchema = ReadOrFallback(
            Path.Combine(schemasRoot, "ComparisonArtifact.v1.schema.json"),
            "schemas/ComparisonArtifact.v1.schema.json");
    }

    /// <summary>
    /// Read from <paramref name="absolutePath"/>; on miss, walk up from the
    /// AppContext.BaseDirectory looking for the repo-root fallback path. This
    /// lets us run before the source-tree files are wired up as MSBuild content.
    /// </summary>
    private static string ReadOrFallback(string absolutePath, string repoRelativeFallback)
    {
        if (File.Exists(absolutePath))
            return File.ReadAllText(absolutePath);

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, repoRelativeFallback);
            if (File.Exists(candidate))
                return File.ReadAllText(candidate);
            dir = dir.Parent;
        }
        throw new FileNotFoundException(
            $"Could not locate '{repoRelativeFallback}' relative to AppContext.BaseDirectory or any ancestor.");
    }
}
