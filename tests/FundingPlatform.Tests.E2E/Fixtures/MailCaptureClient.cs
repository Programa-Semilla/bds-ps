using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FundingPlatform.Tests.E2E.Fixtures;

/// <summary>
/// Spec 021 / T028 / FR-031 / contracts/MailCaptureClient.md — test-side
/// wrapper around the smtp4dev sidecar's REST API. The <see cref="AspireFixture"/>
/// constructs one per test class lifecycle and exposes it as
/// <c>AspireFixture.MailCapture</c>.
///
/// <para>
/// Methods:
/// <list type="bullet">
///   <item><see cref="ListAsync"/> — read all captured messages, optional recipient filter.</item>
///   <item><see cref="WaitForAsync"/> — poll until <c>minCount</c> reached or timeout.</item>
///   <item><see cref="DrainAsync"/> — clear the sidecar between tests for isolation.</item>
/// </list>
/// </para>
/// </summary>
public sealed class MailCaptureClient : IDisposable
{
    private readonly HttpClient _httpClient;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public MailCaptureClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    /// <summary>List all captured messages, optionally filtered by recipient email substring.</summary>
    public async Task<IReadOnlyList<CapturedMessage>> ListAsync(
        string? recipientEmailFilter = null,
        CancellationToken ct = default)
    {
        // smtp4dev's REST API exposes /api/Messages with paging. Pull a wide
        // page (200) since our test batches are small.
        var url = "api/Messages?pageSize=200";
        using var response = await _httpClient.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();
        var page = await response.Content.ReadFromJsonAsync<MessagesPage>(JsonOptions, ct)
                   ?? new MessagesPage();

        var results = new List<CapturedMessage>(page.Results?.Count ?? 0);
        foreach (var summary in page.Results ?? new List<MessageSummary>())
        {
            if (recipientEmailFilter is not null &&
                (summary.To?.IndexOf(recipientEmailFilter, StringComparison.OrdinalIgnoreCase) ?? -1) < 0)
            {
                continue;
            }

            var detail = await FetchDetailAsync(summary.Id, ct);
            if (detail is not null)
            {
                results.Add(detail);
            }
        }
        return results;
    }

    /// <summary>Poll until <c>minCount</c> messages match the filter, or throw on timeout.</summary>
    public async Task<IReadOnlyList<CapturedMessage>> WaitForAsync(
        int minCount,
        TimeSpan timeout,
        Predicate<CapturedMessage>? filter = null,
        CancellationToken ct = default)
    {
        var deadline = DateTime.UtcNow.Add(timeout);
        IReadOnlyList<CapturedMessage> last = Array.Empty<CapturedMessage>();
        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            var all = await ListAsync(ct: ct);
            var matched = filter is null ? all : all.Where(m => filter(m)).ToArray();
            if (matched.Count >= minCount)
            {
                return matched.ToArray();
            }
            last = matched;
            await Task.Delay(250, ct);
        }

        throw new TimeoutException(
            $"MailCaptureClient.WaitForAsync timed out after {timeout.TotalSeconds:F1}s. " +
            $"Required minCount={minCount}, observed={last.Count}.");
    }

    /// <summary>Drain all messages from the sidecar.</summary>
    public async Task DrainAsync(CancellationToken ct = default)
    {
        // smtp4dev exposes DELETE /api/Messages/* (all) — fall back to per-id delete
        // if not supported by the running image version.
        try
        {
            using var response = await _httpClient.DeleteAsync("api/Messages/*", ct);
            if (response.IsSuccessStatusCode) return;
        }
        catch (HttpRequestException) { /* fall through */ }

        // Per-id loop fallback.
        var list = await ListAsync(ct: ct);
        foreach (var msg in list)
        {
            using var r = await _httpClient.DeleteAsync($"api/Messages/{msg.Id}", ct);
            // Ignore individual failures — best-effort drain.
            _ = r;
        }
    }

    private async Task<CapturedMessage?> FetchDetailAsync(string id, CancellationToken ct)
    {
        using var response = await _httpClient.GetAsync($"api/Messages/{id}", ct);
        if (!response.IsSuccessStatusCode) return null;
        var detail = await response.Content.ReadFromJsonAsync<MessageDetail>(JsonOptions, ct);
        if (detail is null) return null;

        var htmlPart = detail.Parts?.FirstOrDefault(p =>
            string.Equals(p.ContentType, "text/html", StringComparison.OrdinalIgnoreCase));
        var textPart = detail.Parts?.FirstOrDefault(p =>
            string.Equals(p.ContentType, "text/plain", StringComparison.OrdinalIgnoreCase));

        return new CapturedMessage(
            Id: detail.Id ?? id,
            FromAddress: detail.From ?? string.Empty,
            FromDisplayName: detail.FromName ?? string.Empty,
            ToAddresses: detail.To?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                               .ToArray() ?? Array.Empty<string>(),
            Subject: detail.Subject ?? string.Empty,
            HtmlBody: htmlPart?.Content ?? string.Empty,
            TextBody: textPart?.Content ?? string.Empty,
            ReceivedAt: detail.ReceivedDate ?? DateTimeOffset.UtcNow,
            Headers: detail.Headers?.ToDictionary(h => h.Name ?? string.Empty, h => h.Value ?? string.Empty)
                     ?? new Dictionary<string, string>());
    }

    public void Dispose() => _httpClient.Dispose();

    // ---- smtp4dev REST DTOs ----

    private sealed class MessagesPage
    {
        public List<MessageSummary>? Results { get; set; }
    }

    private sealed class MessageSummary
    {
        public string Id { get; set; } = string.Empty;
        public string? From { get; set; }
        public string? To { get; set; }
        public string? Subject { get; set; }
        public DateTimeOffset? ReceivedDate { get; set; }
    }

    private sealed class MessageDetail
    {
        public string? Id { get; set; }
        public string? From { get; set; }
        public string? FromName { get; set; }
        public string? To { get; set; }
        public string? Subject { get; set; }
        public DateTimeOffset? ReceivedDate { get; set; }
        public List<MessagePart>? Parts { get; set; }
        public List<MessageHeader>? Headers { get; set; }
    }

    private sealed class MessagePart
    {
        public string? ContentType { get; set; }
        public string? Content { get; set; }
    }

    private sealed class MessageHeader
    {
        public string? Name { get; set; }
        public string? Value { get; set; }
    }
}

/// <summary>Spec 021 / T028 — captured-message shape exposed to tests.</summary>
public sealed record CapturedMessage(
    string Id,
    string FromAddress,
    string FromDisplayName,
    IReadOnlyList<string> ToAddresses,
    string Subject,
    string HtmlBody,
    string TextBody,
    DateTimeOffset ReceivedAt,
    IReadOnlyDictionary<string, string> Headers);
