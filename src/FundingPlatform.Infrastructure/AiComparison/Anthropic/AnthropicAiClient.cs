using System.Diagnostics;
using System.Text.Json;
using Anthropic.SDK;
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

    public async Task<ExtractResult> ExtractAsync(ExtractRequest request, CancellationToken cancellationToken)
    {
        var (json, tokenIn, tokenOut, latencyMs) = await SendAsync(
            request.Model, request.PromptText, request.SchemaJson,
            BuildUserContent(request.Blocks), cancellationToken);
        return new ExtractResult(json, tokenIn, tokenOut, latencyMs);
    }

    public async Task<CompareResult> CompareAsync(CompareRequest request, CancellationToken cancellationToken)
    {
        var (json, tokenIn, tokenOut, latencyMs) = await SendAsync(
            request.Model, request.PromptText, request.SchemaJson,
            new List<ContentBase> { new TextContent { Text = request.NormalizedSuppliersJson } },
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
            }
        }
        return contents;
    }

    private async Task<(string json, int tokenIn, int tokenOut, int latencyMs)> SendAsync(
        string model, string systemPrompt, string schemaJson,
        List<ContentBase> userContent, CancellationToken cancellationToken)
    {
        using var client = string.IsNullOrEmpty(_options.BaseUrl)
            ? new AnthropicClient(new APIAuthentication(_options.ApiKey))
            : new AnthropicClient(new APIAuthentication(_options.ApiKey));

        var parameters = new MessageParameters
        {
            Model = model,
            MaxTokens = 8192,
            System = new List<SystemMessage> { new SystemMessage(systemPrompt) },
            Messages = new List<Message>
            {
                new Message { Role = RoleType.User, Content = userContent },
            },
        };

        var sw = Stopwatch.StartNew();
        MessageResponse response;
        try
        {
            response = await client.Messages.GetClaudeMessageAsync(parameters, cancellationToken);
        }
        catch (OperationCanceledException) { throw; }
        catch (HttpRequestException ex)
        {
            throw new AiProviderTransientException("Network error calling Anthropic API.", ex);
        }
        catch (Exception ex)
        {
            // Anthropic SDK throws subclasses of Exception; classify by message
            // text. 5xx / 429 are transient.
            var msg = ex.Message ?? string.Empty;
            if (msg.Contains("429") || msg.Contains("5xx") || msg.Contains("timed out", StringComparison.OrdinalIgnoreCase))
                throw new AiProviderTransientException(msg, ex);
            throw new AiProviderHardException("unknown", msg, ex);
        }
        sw.Stop();

        var text = response.Content?
            .OfType<TextContent>()
            .Select(t => t.Text)
            .FirstOrDefault();

        if (string.IsNullOrWhiteSpace(text))
            throw new AiProviderHardException("empty_response", "Anthropic returned no text content.");

        // Trim Markdown code-fence wrappers the model occasionally adds.
        text = StripCodeFences(text);

        var usage = response.Usage;
        var tokenIn = (int)(usage?.InputTokens ?? 0);
        var tokenOut = (int)(usage?.OutputTokens ?? 0);
        return (text, tokenIn, tokenOut, (int)sw.ElapsedMilliseconds);
    }

    private static string StripCodeFences(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.StartsWith("```"))
        {
            var firstNewline = trimmed.IndexOf('\n');
            if (firstNewline > 0)
                trimmed = trimmed[(firstNewline + 1)..];
            if (trimmed.EndsWith("```"))
                trimmed = trimmed[..^3];
        }
        return trimmed.Trim();
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
