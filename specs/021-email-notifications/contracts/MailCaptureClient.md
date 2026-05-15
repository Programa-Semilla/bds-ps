# Contract: `MailCaptureClient`

**Layer**: `FundingPlatform.Tests.E2E.Fixtures` (test infrastructure)
**Spec FRs**: FR-031.

## Purpose

`MailCaptureClient` is the test-side wrapper around the smtp4dev sidecar's REST API. The `AspireFixture` exposes it; every notification E2E test consumes it to drain captured messages, count per-recipient deliveries, and assert envelope + body invariants.

## Interface

```csharp
namespace FundingPlatform.Tests.E2E.Fixtures;

public sealed class MailCaptureClient : IDisposable
{
    public MailCaptureClient(HttpClient httpClient);

    /// <summary>
    /// List all captured messages, optionally filtered by recipient email.
    /// </summary>
    public Task<IReadOnlyList<CapturedMessage>> ListAsync(
        string? recipientEmailFilter = null,
        CancellationToken ct = default);

    /// <summary>
    /// Wait up to <paramref name="timeout"/> for at least <paramref name="minCount"/> messages
    /// matching the optional filter to be captured. Polls every 250 ms.
    /// </summary>
    public Task<IReadOnlyList<CapturedMessage>> WaitForAsync(
        int minCount,
        TimeSpan timeout,
        Predicate<CapturedMessage>? filter = null,
        CancellationToken ct = default);

    /// <summary>
    /// Drain (DELETE all messages on the sidecar). Call between tests to keep isolation.
    /// </summary>
    public Task DrainAsync(CancellationToken ct = default);
}

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
```

## smtp4dev REST API mapping

| Method | smtp4dev endpoint |
|---|---|
| `ListAsync` | `GET /api/Messages?searchTerms={recipient}` |
| `GetMessageAsync` (internal) | `GET /api/Messages/{id}` |
| `DrainAsync` | `DELETE /api/Messages/*` (loop the list and delete each, OR `POST /api/Messages/?action=clear` if supported by image version) |

The `HttpClient.BaseAddress` is set by `AspireFixture` to the resolved `smtp4dev` HTTP endpoint URI (`http://localhost:<random>` chosen by Aspire).

## Behavior contract

1. **Test isolation** — every test that consumes `MailCaptureClient` MUST `await DrainAsync()` in `[SetUp]` or via a base-class fixture method. The sidecar is shared across the test class lifecycle.
2. **Polling** — `WaitForAsync` polls every 250 ms with a default timeout of 30 s (configurable). The polling interval is faster than the worker's poll interval (5 s default) so the harness does not race the worker.
3. **Plain-text fallback** — `CapturedMessage.TextBody` is populated from the `text/plain` MIME part. Empty string when the message is HTML-only (which should NEVER happen in this spec; all variants ship both).
4. **Body assertions** — tests perform substring + regex checks against `HtmlBody` and `TextBody`. The fixture does NOT pre-validate; it only surfaces the raw bytes.

## Lifecycle in `AspireFixture`

```csharp
// Inside AspireFixture.StartAsync(), after _app.WaitForResource("smtp4dev", "Healthy"):
var http = _app.GetEndpoint("smtp4dev", "http").Uri;
_mailCaptureHttp = new HttpClient { BaseAddress = http };
MailCapture = new MailCaptureClient(_mailCaptureHttp);
```

`MailCapture` is exposed as a public property on `AspireFixture` for test access. Disposed in `DisposeAsync` alongside the other Aspire client surfaces.

## Test surface

| Test | Asserts |
|---|---|
| `ApplicationSubmittedNotificationsTests` | After Submit: `MailCapture.WaitForAsync(minCount: 1 + #reviewers + #participatingAdmins)` returns each expected recipient with the correct subject + deep link. |
| `ReturnedToApplicantNotificationsTests` | After SendBack: exactly one applicant-variant message captured; reviewer count = 0. |
| `ResubmittedNotificationsTests` | After Submit-following-SendBack: exactly #reviewers messages captured; applicant count = 0; idempotency double-process test does not produce a second message. |
| `ApprovedAndRejectedNotificationsTests` | After Finalize (each outcome): applicant + participating admins captured; reviewer count = 0. |
| `ProviderOutageResilienceTests` | After SIGSTOP / SIGCONT: messages eventually captured; no duplicates. |
| `AllowlistGuardE2ETests` | With `Notifications:NonProdAllowlist=[]`: `MailCapture.ListAsync()` returns 0 messages even after firing an event. |
| `EmailTemplateSenderTests` (re-purposed from `Assert.Ignore`) | For each event variant, assert sender display, signature block, no `<img>`, no `Capital Semilla`/`Forge`, subject template render. |
