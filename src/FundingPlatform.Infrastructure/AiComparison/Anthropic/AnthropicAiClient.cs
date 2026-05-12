using System.Diagnostics;
using System.Net;
using System.Net.Http;
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
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (TaskCanceledException ex)
        {
            // SDK raises TaskCanceledException for upstream HTTP timeouts when
            // the caller's CT is not cancelled — treat as transient (FR-I1).
            throw new AiProviderTransientException("Anthropic request timed out.", ex);
        }
        catch (TimeoutException ex)
        {
            throw new AiProviderTransientException("Anthropic request timed out.", ex);
        }
        catch (HttpRequestException ex)
        {
            throw ClassifyHttpFailure(ex);
        }
        catch (Exception ex)
        {
            // Anthropic.SDK has no typed exception hierarchy with a status code
            // (only standard System types). If a status code surfaces in the
            // message, classify it; otherwise fail-safe to transient so the user
            // sees "Reintentar" rather than "Contacte un administrador".
            var classified = TryClassifyByMessage(ex);
            if (classified is not null) throw classified;
            throw new AiProviderTransientException(ex.Message, ex);
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
