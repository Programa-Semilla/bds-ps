using System.Text.Json;
using FundingPlatform.Application.Abstractions.AiComparison;
using Json.Schema;

namespace FundingPlatform.Application.AiComparison;

/// <summary>
/// Spec 020 / FR-C4 / FR-I3 — wraps JsonSchema.Net to validate AI responses.
/// Throws <see cref="AiSchemaInvalidException"/> on failure with the validator's
/// first error path so the audit row can record it.
/// </summary>
public class SchemaValidator
{
    private readonly JsonSchema _extractSchema;
    private readonly JsonSchema _compareSchema;

    public SchemaValidator(PromptCatalog catalog)
    {
        // Parse to JsonNode then to JsonSchema so we don't hit the global
        // SchemaRegistry's "Overwriting registered schemas is not permitted"
        // when SchemaValidator is constructed more than once in-process
        // (e.g. test runs that recreate the singleton).
        var extractDoc = System.Text.Json.JsonDocument.Parse(catalog.ExtractSchema);
        var compareDoc = System.Text.Json.JsonDocument.Parse(catalog.CompareSchema);
        _extractSchema = JsonSchema.FromText(StripId(extractDoc.RootElement.GetRawText()));
        _compareSchema = JsonSchema.FromText(StripId(compareDoc.RootElement.GetRawText()));
    }

    /// <summary>
    /// JsonSchema.Net registers each schema by its <c>$id</c> globally. Strip
    /// the property so re-construction in tests (or hot-reload) doesn't fail
    /// with "Overwriting registered schemas is not permitted".
    /// </summary>
    private static string StripId(string raw)
    {
        using var doc = System.Text.Json.JsonDocument.Parse(raw);
        using var ms = new MemoryStream();
        using var writer = new System.Text.Json.Utf8JsonWriter(ms);
        writer.WriteStartObject();
        foreach (var prop in doc.RootElement.EnumerateObject())
        {
            if (prop.NameEquals("$id")) continue;
            prop.WriteTo(writer);
        }
        writer.WriteEndObject();
        writer.Flush();
        return System.Text.Encoding.UTF8.GetString(ms.ToArray());
    }

    public void ValidateExtract(string json) => Validate(_extractSchema, json, "extract");
    public void ValidateCompare(string json) => Validate(_compareSchema, json, "compare");

    private static void Validate(JsonSchema schema, string json, string label)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new AiSchemaInvalidException("$", $"AI {label} response was not valid JSON: {ex.Message}");
        }

        var results = schema.Evaluate(doc.RootElement, new EvaluationOptions
        {
            OutputFormat = OutputFormat.List,
        });

        if (results.IsValid) return;

        var firstError = FirstError(results);
        throw new AiSchemaInvalidException(
            firstError.path,
            $"AI {label} response failed schema validation at {firstError.path}: {firstError.message}");
    }

    private static (string path, string message) FirstError(EvaluationResults results)
    {
        // Walk the details tree, prefer the deepest failing node so the
        // reported path is the most useful (e.g. items[0].suppliers).
        var stack = new Stack<EvaluationResults>();
        stack.Push(results);
        EvaluationResults? worst = null;
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            if (!node.IsValid && node.Errors is { Count: > 0 })
                worst = node;
            if (node.Details is not null)
            {
                foreach (var child in node.Details)
                    stack.Push(child);
            }
        }

        if (worst is null)
            return ("$", "schema validation failed");

        var path = worst.InstanceLocation.ToString();
        if (string.IsNullOrEmpty(path)) path = "$";

        var firstError = worst.Errors?.Values.FirstOrDefault() ?? "schema validation failed";
        return (path, firstError);
    }
}
