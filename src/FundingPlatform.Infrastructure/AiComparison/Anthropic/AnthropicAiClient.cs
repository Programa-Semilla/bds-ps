using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using Anthropic.SDK;
using Anthropic.SDK.Common;
using Anthropic.SDK.Messaging;
using FundingPlatform.Application.Abstractions.AiComparison;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FundingPlatform.Infrastructure.AiComparison.Anthropic;

/// <summary>
/// Spec 020 — Anthropic Claude implementation of <see cref="IAiClient"/>.
/// Wraps <c>Anthropic.SDK.AnthropicClient</c>. No retry. Surfaces transient
/// vs hard provider errors as typed exceptions (FR-I1/I2). Never logs raw
/// bodies; only token counts + latency for the audit row.
///
/// NFR-S1 carve-out: the provider's own <i>error</i> text is logged on failure
/// (see <see cref="LogProviderFailure"/>). Only the numeric status survives
/// into <see cref="AiProviderHardException.ProviderCode"/> and the audit
/// payload, so without this the sentence that explains the failure — expired
/// key, model not available, credit balance too low — is discarded and the
/// operator is left with a bare "provider_hard:400". Anthropic error messages
/// describe the request envelope, not its contents; request payloads, prompts,
/// and redacted supplier documents are still never logged.
/// </summary>
public class AnthropicAiClient : IAiClient
{
    private readonly AnthropicOptions _options;
    private readonly ILogger<AnthropicAiClient> _logger;

    public AnthropicAiClient(IOptions<AnthropicOptions> options, ILogger<AnthropicAiClient> logger)
    {
        _options = options.Value;
        _logger = logger;

        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            throw new InvalidOperationException(
                "AiComparison:Anthropic:ApiKey is required when AiComparison:Provider=Anthropic.");
        }
    }

    // Tool names exposed to the model. ToolChoice = Tool with one of these names
    // forces the model to emit a single tool_use block whose `input` is bound to
    // our JSON schema — the AI cannot improvise an off-schema response.
    internal const string ExtractToolName = "extract_supplier_offering";
    internal const string CompareToolName = "record_comparison_artifact";

    public async Task<ExtractResult> ExtractAsync(ExtractRequest request, CancellationToken cancellationToken)
    {
        var (json, tokenIn, tokenOut, latencyMs) = await SendAsync(
            request.Model, request.PromptText, request.SchemaJson,
            BuildUserContent(request.Blocks),
            ExtractToolName,
            "Devuelve la cotización extraída del proveedor como objeto JSON estructurado, conforme al esquema.",
            cancellationToken);
        return new ExtractResult(json, tokenIn, tokenOut, latencyMs);
    }

    public async Task<CompareResult> CompareAsync(CompareRequest request, CancellationToken cancellationToken)
    {
        var (json, tokenIn, tokenOut, latencyMs) = await SendAsync(
            request.Model, request.PromptText, request.SchemaJson,
            new List<ContentBase> { new TextContent { Text = request.NormalizedSuppliersJson } },
            CompareToolName,
            "Devuelve la comparación de cotizaciones como objeto JSON estructurado, conforme al esquema.",
            cancellationToken);
        return new CompareResult(json, tokenIn, tokenOut, latencyMs);
    }

    private static List<ContentBase> BuildUserContent(IReadOnlyList<AiInputBlock> blocks)
    {
        var contents = new List<ContentBase>(blocks.Count);
        foreach (var block in blocks)
        {
            switch (block)
            {
                case TextBlock t:
                    contents.Add(new TextContent { Text = t.Text });
                    break;
                case PdfBlock p:
                    contents.Add(new DocumentContent
                    {
                        Source = new DocumentSource
                        {
                            MediaType = "application/pdf",
                            Data = Convert.ToBase64String(p.Bytes.Span),
                        },
                    });
                    break;
                case ImageBlock img:
                    // Images must go up as an image block, not a document block —
                    // image bytes declared as application/pdf are rejected with
                    // "The PDF specified was not valid" and fail the whole call.
                    contents.Add(new ImageContent
                    {
                        Source = new ImageSource
                        {
                            MediaType = img.MediaType,
                            Data = Convert.ToBase64String(img.Bytes.Span),
                        },
                    });
                    break;
            }
        }
        return contents;
    }

    private async Task<(string json, int tokenIn, int tokenOut, int latencyMs)> SendAsync(
        string model, string systemPrompt, string schemaJson,
        List<ContentBase> userContent, string toolName, string toolDescription,
        CancellationToken cancellationToken)
    {
        using var client = string.IsNullOrEmpty(_options.BaseUrl)
            ? new AnthropicClient(new APIAuthentication(_options.ApiKey))
            : new AnthropicClient(new APIAuthentication(_options.ApiKey));

        // Bind our JSON Schema to a tool's `input_schema` and force ToolChoice
        // so the model must emit a single tool_use block whose `input` matches
        // the schema. This is the structured-output contract — without it the
        // model improvises free-form JSON and misses required fields.
        var schemaNode = BuildToolInputSchema(schemaJson);
        var tool = new global::Anthropic.SDK.Common.Tool(
            new global::Anthropic.SDK.Common.Function(toolName, toolDescription, schemaNode));

        var parameters = new MessageParameters
        {
            Model = model,
            MaxTokens = 8192,
            System = new List<SystemMessage> { new SystemMessage(systemPrompt) },
            Messages = new List<Message>
            {
                new Message { Role = RoleType.User, Content = userContent },
            },
            Tools = new List<global::Anthropic.SDK.Common.Tool> { tool },
            ToolChoice = new ToolChoice
            {
                Type = ToolChoiceType.Tool,
                Name = toolName,
            },
        };

        var sw = Stopwatch.StartNew();
        MessageResponse response;
        try
        {
            response = await client.Messages.GetClaudeMessageAsync(parameters, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (TaskCanceledException ex)
        {
            // SDK raises TaskCanceledException for upstream HTTP timeouts when
            // the caller's CT is not cancelled — treat as transient (FR-I1).
            throw LogProviderFailure(model, toolName, new AiProviderTransientException("Anthropic request timed out.", ex), ex);
        }
        catch (TimeoutException ex)
        {
            throw LogProviderFailure(model, toolName, new AiProviderTransientException("Anthropic request timed out.", ex), ex);
        }
        catch (HttpRequestException ex)
        {
            throw LogProviderFailure(model, toolName, ClassifyHttpFailure(ex), ex);
        }
        catch (Exception ex)
        {
            // Anthropic.SDK has no typed exception hierarchy with a status code
            // (only standard System types). If a status code surfaces in the
            // message, classify it; otherwise fail-safe to transient so the user
            // sees "Reintentar" rather than "Contacte un administrador".
            var classified = TryClassifyByMessage(ex) ?? new AiProviderTransientException(ex.Message, ex);
            throw LogProviderFailure(model, toolName, classified, ex);
        }
        sw.Stop();

        var toolUse = response.Content?
            .OfType<ToolUseContent>()
            .FirstOrDefault(c => string.Equals(c.Name, toolName, StringComparison.Ordinal));

        if (toolUse?.Input is null)
        {
            // Fallback: some failure modes (refusal, max_tokens before tool call)
            // produce text instead of a tool_use block. Surface as hard so the
            // orchestrator records a precise reason.
            var stopReason = response.StopReason ?? "unknown";
            throw LogProviderFailure(model, toolName, new AiProviderHardException(
                $"no_tool_call:{stopReason}",
                $"Anthropic did not emit the forced '{toolName}' tool call (stop_reason={stopReason})."), provider: null);
        }

        var text = toolUse.Input.ToJsonString();

        var usage = response.Usage;
        var tokenIn = (int)(usage?.InputTokens ?? 0);
        var tokenOut = (int)(usage?.OutputTokens ?? 0);
        return (text, tokenIn, tokenOut, (int)sw.ElapsedMilliseconds);
    }

    /// <summary>
    /// Anthropic's <c>input_schema</c> field accepts a JSON Schema subset. Strip
    /// the meta keys that aren't recognized by the API (<c>$schema</c>,
    /// <c>$id</c>, <c>title</c>, <c>description</c> at root) before passing —
    /// keeps the payload lean and avoids "unexpected field" warnings on stricter
    /// validators. The <c>$defs</c> / <c>$ref</c> structure is preserved.
    /// </summary>
    private static JsonNode BuildToolInputSchema(string schemaJson)
    {
        var node = JsonNode.Parse(schemaJson)
            ?? throw new InvalidOperationException("Schema JSON parsed to null.");
        if (node is JsonObject root)
        {
            root.Remove("$schema");
            root.Remove("$id");
            root.Remove("title");
            root.Remove("description");
        }
        return node;
    }

    /// <summary>
    /// Writes the provider's own failure text to the log, then returns the
    /// classified exception so call sites read <c>throw LogProviderFailure(...)</c>.
    ///
    /// Hard failures are operator-actionable (bad key, model not available,
    /// billing) and log at Error; transient ones log at Warning. Both clear the
    /// <c>Warning</c> default log level pinned in <c>deploy/vm/docker-compose.yml</c>,
    /// so no deployment config change is needed to see them.
    /// </summary>
    private TException LogProviderFailure<TException>(
        string model, string toolName, TException classified, Exception? provider)
        where TException : Exception
    {
        var providerMessage = provider?.Message ?? classified.Message;

        if (classified is AiProviderHardException hard)
        {
            _logger.LogError(provider,
                "Anthropic call failed (hard): model={Model} tool={Tool} providerCode={ProviderCode}. Provider said: {ProviderMessage}",
                model, toolName, hard.ProviderCode, providerMessage);
        }
        else
        {
            _logger.LogWarning(provider,
                "Anthropic call failed (transient, retryable): model={Model} tool={Tool}. Provider said: {ProviderMessage}",
                model, toolName, providerMessage);
        }

        return classified;
    }

    /// <summary>
    /// FR-I1 / FR-I2 — map an <see cref="HttpRequestException"/> raised from the
    /// Anthropic SDK onto the typed provider exceptions:
    /// 5xx / 408 / 429 → transient (retryable);
    /// other 4xx → hard with the concrete status code embedded;
    /// unknown (StatusCode null) → fail-safe transient.
    /// </summary>
    private static Exception ClassifyHttpFailure(HttpRequestException ex)
    {
        // .NET 5+ HttpRequestException exposes StatusCode when the SDK uses
        // HttpClient.SendAsync + EnsureSuccessStatusCode-style flows.
        var status = ex.StatusCode;
        if (status is null)
        {
            // No status — DNS / TLS / socket failure. Treat as transient.
            return new AiProviderTransientException("Network error calling Anthropic API.", ex);
        }

        var code = (int)status.Value;
        if (code >= 500 || code == 408 || code == 429)
        {
            return new AiProviderTransientException(
                $"Anthropic returned HTTP {code} ({status.Value}); retryable.", ex);
        }

        return new AiProviderHardException(code.ToString(), $"Anthropic returned HTTP {code} ({status.Value}).", ex);
    }

    /// <summary>
    /// Backstop classifier for exceptions that don't carry a typed status code.
    /// Matches `429`, `5xx`, or any explicit 3-digit 5xx pattern in the message
    /// before giving up. Returns <c>null</c> when no classification could be
    /// inferred — caller falls back to transient.
    /// </summary>
    private static Exception? TryClassifyByMessage(Exception ex)
    {
        var msg = ex.Message ?? string.Empty;
        // Match a 3-digit code in the message.
        var match = System.Text.RegularExpressions.Regex.Match(msg, @"\b(\d{3})\b");
        if (!match.Success) return null;
        if (!int.TryParse(match.Groups[1].Value, out var code)) return null;
        if (code is < 100 or > 599) return null;

        if (code >= 500 || code == 408 || code == 429)
            return new AiProviderTransientException(msg, ex);
        if (code >= 400)
            return new AiProviderHardException(code.ToString(), msg, ex);
        return null;
    }

}

/// <summary>Bound from <c>AiComparison:Anthropic:*</c>.</summary>
public class AnthropicOptions
{
    public string ApiKey { get; set; } = string.Empty;
    public string ExtractModel { get; set; } = "claude-sonnet-4-6";
    public string CompareModel { get; set; } = "claude-opus-4-7";
    public string? BaseUrl { get; set; }
}
