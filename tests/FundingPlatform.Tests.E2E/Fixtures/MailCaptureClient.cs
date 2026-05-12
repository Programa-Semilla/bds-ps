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
            if (recipientEmailFilter is not null)
            {
                var summaryToString = string.Join(",", summary.To ?? new List<string>());
                if (summaryToString.IndexOf(recipientEmailFilter, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }
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

        // smtp4dev's MessageEntitySummary tree carries metadata only; the part body
        // is served separately at /api/Messages/{id}/part/{partId}/content. Walk
        // the tree recursively, find the first text/html and text/plain parts
        // (via the part's Content-Type header), and fetch their bodies.
        var flatParts = FlattenParts(detail.Parts).ToList();
        var htmlPart = flatParts.FirstOrDefault(p => MatchesContentType(p, "text/html"));
        var textPart = flatParts.FirstOrDefault(p => MatchesContentType(p, "text/plain"));

        var htmlBody = htmlPart is null ? string.Empty : await FetchPartContentAsync(id, htmlPart.Id ?? string.Empty, ct);
        var textBody = textPart is null ? string.Empty : await FetchPartContentAsync(id, textPart.Id ?? string.Empty, ct);

        var fromAddress = detail.From ?? string.Empty;
        return new CapturedMessage(
            Id: detail.Id ?? id,
            FromAddress: fromAddress,
            FromDisplayName: ExtractDisplayName(fromAddress),
            ToAddresses: detail.To?.ToArray() ?? Array.Empty<string>(),
            Subject: detail.Subject ?? string.Empty,
            HtmlBody: htmlBody,
            TextBody: textBody,
            ReceivedAt: detail.ReceivedDate ?? DateTimeOffset.UtcNow,
            Headers: detail.Headers?.ToDictionary(h => h.Name ?? string.Empty, h => h.Value ?? string.Empty)
                     ?? new Dictionary<string, string>());
    }

    private static IEnumerable<MessageEntitySummary> FlattenParts(IEnumerable<MessageEntitySummary>? parts)
    {
        if (parts is null) yield break;
        foreach (var p in parts)
        {
            yield return p;
            foreach (var child in FlattenParts(p.ChildParts))
            {
                yield return child;
            }
        }
    }

    private static bool MatchesContentType(MessageEntitySummary part, string mediaType)
    {
        // smtp4dev encodes the part's Content-Type as a header on the part itself.
        // Match the prefix so charset / boundary params don't break the lookup.
        var ct = part.Headers?.FirstOrDefault(h =>
            string.Equals(h.Name, "Content-Type", StringComparison.OrdinalIgnoreCase))?.Value
            ?? string.Empty;
        return ct.StartsWith(mediaType, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<string> FetchPartContentAsync(string messageId, string partId, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(partId)) return string.Empty;
        using var response = await _httpClient.GetAsync($"api/Messages/{messageId}/part/{partId}/content", ct);
        if (!response.IsSuccessStatusCode) return string.Empty;
        return await response.Content.ReadAsStringAsync(ct);
    }

    private static string ExtractDisplayName(string from)
    {
        // RFC 5322 address: '"Display Name" <email@example.com>' or 'email@example.com'.
        // Strip surrounding quotes around the name part.
        var idx = from.IndexOf('<');
        if (idx <= 0) return string.Empty;
        var raw = from[..idx].Trim();
        if (raw.StartsWith('"') && raw.EndsWith('"') && raw.Length >= 2)
        {
            return raw[1..^1];
        }
        return raw;
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
        // smtp4dev exposes From as a string and To as an array of strings in /api/Messages.
        public string? From { get; set; }
        public List<string>? To { get; set; }
        public string? Subject { get; set; }
        public DateTimeOffset? ReceivedDate { get; set; }
    }

    private sealed class MessageDetail
    {
        public string? Id { get; set; }
        public string? From { get; set; }
        // smtp4dev (3.6.x) returns `to` as an array of strings on both the
        // summary and detail endpoints.
        public List<string>? To { get; set; }
        public string? Subject { get; set; }
        public DateTimeOffset? ReceivedDate { get; set; }
        public List<MessageEntitySummary>? Parts { get; set; }
        public List<MessageHeader>? Headers { get; set; }
    }

    private sealed class MessageEntitySummary
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
        public string? ContentId { get; set; }
        public List<MessageHeader>? Headers { get; set; }
        public List<MessageEntitySummary>? ChildParts { get; set; }
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
