# Contract: `IEmailSender`

**Layer**: `FundingPlatform.Application` (interface) / `FundingPlatform.Infrastructure` (implementations)
**Spec FRs**: FR-014, FR-015, FR-016, FR-017 (decorator), FR-021 (transient), FR-022 (permanent), FR-028.

## Interface

```csharp
namespace FundingPlatform.Application.Notifications;

public interface IEmailSender
{
    /// <summary>
    /// Send one fully-rendered email to one recipient.
    /// </summary>
    /// <returns>
    /// <see cref="EmailSendResult"/> classifying the outcome.
    /// MUST NOT throw on provider-side failures — return Transient or Permanent.
    /// MAY throw on programmer errors (null request, malformed envelope).
    /// </returns>
    Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken ct);
}
```

## Types

```csharp
public sealed record EmailMessage(
    string ToEmail,
    string ToDisplayName,
    string Subject,
    string HtmlBody,
    string TextBody,
    string? ReplyTo,
    IReadOnlyDictionary<string, string>? Headers);

public sealed record EmailSendResult(
    EmailSendOutcome Outcome,
    string? ProviderMessageId,
    string? ErrorMessage);

public enum EmailSendOutcome
{
    Sent              = 1, // accepted by the provider
    TransientFailure  = 2, // retry per backoff schedule (timeout, 5xx, 429)
    PermanentFailure  = 3, // dead-letter immediately (4xx, hard bounce, render exception)
    BlockedByAllowlist = 4 // dropped by RecipientAllowlistFilter; never reaches the wrapped sender
}
```

## Behavior contract

1. **Idempotency** — the sender itself is NOT responsible for idempotency. The caller (worker) checks the dedup unique index before invoking `SendAsync`.
2. **Error classification** — the implementation MUST map provider errors per the table below. The worker reads `Outcome` to decide retry vs dead-letter.

   | Provider signal | Outcome |
   |---|---|
   | HTTP 2xx (Mailgun) / SMTP 2xx (Mailtrap) | `Sent` |
   | HTTP 4xx (Mailgun) — except 429 | `PermanentFailure` |
   | HTTP 429 (Mailgun) | `TransientFailure` |
   | HTTP 5xx (Mailgun) | `TransientFailure` |
   | SMTP 5xx — except 552/521/550 | `TransientFailure` |
   | SMTP 552/521/550 (Mailtrap permanent bounces) | `PermanentFailure` |
   | Timeout (Mailgun / Mailtrap) | `TransientFailure` |
   | DNS / connection refused | `TransientFailure` |
   | MailKit `SmtpCommandException.IsFatal` | `PermanentFailure` |
   | Razor render exception thrown upstream by `RazorEmailRenderer` | `PermanentFailure` — surfaced from the caller, not from the sender |

3. **Observability** — every send call MUST log:
   - structured `notifications.send_attempt` (provider, recipient_email, subject_hash)
   - structured `notifications.send_outcome` (provider, recipient_email, outcome, provider_message_id, elapsed_ms)
   No PII beyond `recipient_email` (already in audit per FR-028).

4. **Cancellation** — implementations MUST honor the `CancellationToken`. Worker cancellation MUST not corrupt the outbox row's `Status=Dispatching` state; the next poll re-claims.

5. **Headers** — implementations MUST pass through the `Headers` dictionary verbatim (used by `MailgunHttpEmailSender` for `X-Mailgun-Variables`). The `MailtrapSmtpEmailSender` MUST set RFC-5322 standard headers (`From`, `To`, `Subject`, `Reply-To`, `MIME-Version`, `Content-Type: multipart/alternative`).

6. **No retry inside the sender** — the worker owns the retry loop. Implementations MUST NOT introduce internal retries (single network attempt per `SendAsync`).

## Implementations

### `MailtrapSmtpEmailSender`

- Project: `FundingPlatform.Infrastructure`
- Backed by: **MailKit v3** (MIT) — `MailKit.Net.Smtp.SmtpClient`
- Reads config: `Notifications:Mailtrap:Host`, `Notifications:Mailtrap:Port`, `Notifications:Mailtrap:Username` (nullable), `Notifications:Mailtrap:Password` (nullable), `Notifications:Sender:Email`, `Notifications:Sender:Name`
- In Local with the Aspire-discovered smtp4dev, Host/Port come from the resolved endpoint env vars; Username/Password are empty (smtp4dev accepts any auth).
- Builds a `MimeMessage` with `multipart/alternative` body (HTML + plain text).

### `MailgunHttpEmailSender`

- Project: `FundingPlatform.Infrastructure`
- Backed by: raw `HttpClient` (no Mailgun NuGet)
- Reads config: `Notifications:Mailgun:ApiKey`, `Notifications:Mailgun:Domain`, `Notifications:Mailgun:BaseUrl` (default `https://api.mailgun.net/v3`), `Notifications:Sender:Email`, `Notifications:Sender:Name`
- POSTs to `${BaseUrl}/${Domain}/messages` with Basic auth `api:${ApiKey}` and `multipart/form-data` body (`from`, `to`, `subject`, `html`, `text`, plus `h:Reply-To`, `o:tag`).
- Maps response JSON `id` → `ProviderMessageId`.

### `NoOpEmailSender`

- Project: `FundingPlatform.Infrastructure`
- Logs WARN with the would-be subject + recipient, returns `Outcome=Sent` with `ProviderMessageId=null`.
- Selected automatically in non-Production when no provider config is present (FR-015).
- Selected in Production ONLY by explicit `Notifications:Provider=NoOp` for emergency disable — AppHost MUST log a CRIT line on boot if this configuration is encountered.

### `RecipientAllowlistFilter` (decorator)

- Project: `FundingPlatform.Infrastructure`
- Wraps any `IEmailSender` instance.
- Reads config: `Notifications:NonProdAllowlist` (string array, exact email OR `@domain`).
- Returns `Outcome=BlockedByAllowlist` and DOES NOT invoke the wrapped sender when the recipient is not allowlisted.
- Registered in the DI container ONLY when `HostEnvironment != "Production"`. Production resolves the bare sender directly (FR-019).

## Selection at boot (`NotificationsServiceCollectionExtensions`)

```text
HostEnvironment    Notifications:Provider    Effective binding
─────────────────  ────────────────────────  ────────────────────────────────────────────────
Development        (any/unset)               RecipientAllowlistFilter(MailtrapSmtpEmailSender → smtp4dev sidecar)
Development        Mailgun (explicit)        RecipientAllowlistFilter(MailgunHttpEmailSender)
Development        NoOp                      RecipientAllowlistFilter(NoOpEmailSender)
Staging            Mailgun (default)         RecipientAllowlistFilter(MailgunHttpEmailSender)
Staging            NoOp                      RecipientAllowlistFilter(NoOpEmailSender)
Production         Mailgun (required)        MailgunHttpEmailSender (NO decorator)
Production         missing required config   FAIL FAST at boot (FR-016)
Production         NoOp (explicit)           NoOpEmailSender (NO decorator) + CRIT log line
```

## Test surface

| Test | Layer | Asserts |
|---|---|---|
| `RecipientAllowlistFilterTests` | Unit | Drop / pass-through / production-bypass. Records `BlockedByAllowlist` outcome. |
| `MailgunHttpEmailSenderTests` | Unit | Error classification table above. Uses `HttpMessageHandler` mock. |
| `MailtrapSmtpEmailSenderTests` | Unit | RFC-5322 envelope + multipart/alternative shape. Uses `MimeMessage` round-trip. |
| `NotificationsBootTests` | Integration | Production boot fails when Mailgun config is incomplete (FR-016). |
