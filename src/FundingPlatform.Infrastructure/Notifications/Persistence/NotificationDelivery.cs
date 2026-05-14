using FundingPlatform.Domain.Notifications;

namespace FundingPlatform.Infrastructure.Notifications.Persistence;

/// <summary>
/// Spec 021 / T018 / FR-028 — one row per (outbox row, recipient) pair.
/// The filtered unique index on
/// (EventType, ApplicationId, VersionHistoryId, RecipientUserId) is the
/// idempotency guard (FR-020). Factory methods enforce status invariants.
/// </summary>
public class NotificationDelivery
{
    private NotificationDelivery() { }

    /// <summary>FR-028 — successful send recorded by the worker.</summary>
    public static NotificationDelivery RecordSend(
        long outboxId,
        NotificationEvent eventType,
        int applicationId,
        int versionHistoryId,
        string? recipientUserId,
        string recipientEmail,
        string provider,
        string? providerMessageId,
        int attemptCount,
        DateTime sentAt) =>
        new()
        {
            OutboxId = outboxId,
            EventType = eventType.ToStorageString(),
            ApplicationId = applicationId,
            VersionHistoryId = versionHistoryId,
            RecipientUserId = recipientUserId,
            RecipientEmail = recipientEmail,
            Provider = provider,
            ProviderMessageId = providerMessageId,
            Status = NotificationDeliveryStatus.Sent,
            AttemptCount = attemptCount,
            LastError = null,
            SentAt = sentAt,
        };

    /// <summary>FR-021 — transient failure recorded with the latest attempt count.</summary>
    public static NotificationDelivery RecordTransientFailure(
        long outboxId,
        NotificationEvent eventType,
        int applicationId,
        int versionHistoryId,
        string? recipientUserId,
        string recipientEmail,
        string provider,
        int attemptCount,
        string error) =>
        new()
        {
            OutboxId = outboxId,
            EventType = eventType.ToStorageString(),
            ApplicationId = applicationId,
            VersionHistoryId = versionHistoryId,
            RecipientUserId = recipientUserId,
            RecipientEmail = recipientEmail,
            Provider = provider,
            Status = NotificationDeliveryStatus.Failed,
            AttemptCount = attemptCount,
            LastError = Truncate(error),
            SentAt = null,
        };

    /// <summary>FR-022 — permanent failure / dead-letter.</summary>
    public static NotificationDelivery RecordPermanentFailure(
        long outboxId,
        NotificationEvent eventType,
        int applicationId,
        int versionHistoryId,
        string? recipientUserId,
        string recipientEmail,
        string provider,
        int attemptCount,
        string error) =>
        new()
        {
            OutboxId = outboxId,
            EventType = eventType.ToStorageString(),
            ApplicationId = applicationId,
            VersionHistoryId = versionHistoryId,
            RecipientUserId = recipientUserId,
            RecipientEmail = recipientEmail,
            Provider = provider,
            Status = NotificationDeliveryStatus.DeadLetter,
            AttemptCount = attemptCount,
            LastError = Truncate(error),
            SentAt = null,
        };

    /// <summary>FR-029 — null or empty email skipped without contacting the provider.</summary>
    public static NotificationDelivery RecordSkipped(
        long outboxId,
        NotificationEvent eventType,
        int applicationId,
        int versionHistoryId,
        string? recipientUserId,
        string recipientEmail,
        string provider,
        string reason) =>
        new()
        {
            OutboxId = outboxId,
            EventType = eventType.ToStorageString(),
            ApplicationId = applicationId,
            VersionHistoryId = versionHistoryId,
            RecipientUserId = recipientUserId,
            RecipientEmail = recipientEmail,
            Provider = provider,
            Status = NotificationDeliveryStatus.Skipped,
            AttemptCount = 0,
            LastError = Truncate(reason),
            SentAt = null,
        };

    /// <summary>FR-017 — non-prod allowlist filter blocked the recipient.</summary>
    public static NotificationDelivery RecordBlockedByAllowlist(
        long outboxId,
        NotificationEvent eventType,
        int applicationId,
        int versionHistoryId,
        string? recipientUserId,
        string recipientEmail,
        string provider) =>
        new()
        {
            OutboxId = outboxId,
            EventType = eventType.ToStorageString(),
            ApplicationId = applicationId,
            VersionHistoryId = versionHistoryId,
            RecipientUserId = recipientUserId,
            RecipientEmail = recipientEmail,
            Provider = provider,
            Status = NotificationDeliveryStatus.BlockedByAllowlist,
            AttemptCount = 0,
            LastError = "NotAllowlisted",
            SentAt = null,
        };

    public long Id { get; private set; }
    public long OutboxId { get; private set; }
    public string EventType { get; private set; } = default!;
    public int ApplicationId { get; private set; }
    public int VersionHistoryId { get; private set; }
    public string? RecipientUserId { get; private set; }
    public string RecipientEmail { get; private set; } = default!;
    public string Provider { get; private set; } = default!;
    public string? ProviderMessageId { get; private set; }
    public string Status { get; private set; } = default!;
    public int AttemptCount { get; private set; }
    public string? LastError { get; private set; }
    public DateTime? SentAt { get; private set; }

    private static string? Truncate(string? value)
    {
        if (value is null) return null;
        const int max = 2000;
        return value.Length <= max ? value : value[..max];
    }
}

/// <summary>Spec 021 / data-model.md — string constants matching the dacpac CHECK constraint.</summary>
public static class NotificationDeliveryStatus
{
    public const string Sent               = "Sent";
    public const string Failed             = "Failed";
    public const string DeadLetter         = "DeadLetter";
    public const string BlockedByAllowlist = "BlockedByAllowlist";
    public const string Skipped            = "Skipped";
}

/// <summary>Spec 021 / FR-014 — string constants for the Provider column.</summary>
public static class NotificationDeliveryProvider
{
    public const string MailtrapSmtp = "MailtrapSmtp";
    public const string Mailgun      = "Mailgun";
    public const string NoOp         = "NoOp";
}
