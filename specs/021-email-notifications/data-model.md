# Data Model: Email Notifications System

**Date**: 2026-05-12
**Spec FRs covered**: FR-002, FR-020, FR-028, FR-029, NFR-005, SC-008.

## Schema (dacpac — `FundingPlatform.Database/Tables/`)

### `dbo.NotificationOutbox.sql`

```sql
CREATE TABLE [dbo].[NotificationOutbox]
(
    [Id]                BIGINT           IDENTITY(1,1) NOT NULL,
    [EventType]         VARCHAR(64)      NOT NULL,                          -- see NotificationEvent enum
    [ApplicationId]     INT              NOT NULL,
    [VersionHistoryId]  INT              NOT NULL,                          -- FK to VersionHistory.Id; identifies the workflow row that triggered this notification
    [PayloadJson]       NVARCHAR(MAX)    NOT NULL,                          -- denormalized snapshot (ApplicationId, ApplicantUserId, ApplicantDisplayName, StageGroupIds, derived BaseUrl-relative deep link tokens)
    [CreatedAt]         DATETIME2(3)     NOT NULL CONSTRAINT [DF_NotificationOutbox_CreatedAt] DEFAULT (SYSUTCDATETIME()),
    [Status]            VARCHAR(16)      NOT NULL CONSTRAINT [DF_NotificationOutbox_Status]     DEFAULT ('Pending'),  -- Pending | Dispatching | Done | DeadLetter
    [AttemptCount]      INT              NOT NULL CONSTRAINT [DF_NotificationOutbox_AttemptCount] DEFAULT (0),
    [LastError]         NVARCHAR(2000)   NULL,
    [NextAttemptAt]     DATETIME2(3)     NULL,                              -- nullable: NULL means "claim eagerly on next poll"
    [RowVersion]        ROWVERSION       NOT NULL,                          -- optimistic concurrency for worker claim

    CONSTRAINT [PK_NotificationOutbox] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [FK_NotificationOutbox_Applications]
        FOREIGN KEY ([ApplicationId]) REFERENCES [dbo].[Applications] ([Id]) ON DELETE CASCADE,
    CONSTRAINT [FK_NotificationOutbox_VersionHistory]
        FOREIGN KEY ([VersionHistoryId]) REFERENCES [dbo].[VersionHistory] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [CK_NotificationOutbox_Status]
        CHECK ([Status] IN ('Pending', 'Dispatching', 'Done', 'DeadLetter'))
);
GO

-- Worker poll predicate: hot path for finding work-to-do.
CREATE INDEX [IX_NotificationOutbox_Status_NextAttemptAt]
    ON [dbo].[NotificationOutbox] ([Status], [NextAttemptAt])
    INCLUDE ([Id], [EventType], [ApplicationId], [VersionHistoryId], [AttemptCount]);
GO

-- Operational lookup: "give me everything for application X".
CREATE INDEX [IX_NotificationOutbox_ApplicationId]
    ON [dbo].[NotificationOutbox] ([ApplicationId], [CreatedAt] DESC);
GO
```

### `dbo.NotificationDelivery.sql`

```sql
CREATE TABLE [dbo].[NotificationDelivery]
(
    [Id]                BIGINT           IDENTITY(1,1) NOT NULL,
    [OutboxId]          BIGINT           NOT NULL,
    [EventType]         VARCHAR(64)      NOT NULL,                          -- denormalized from outbox for the idempotency key
    [ApplicationId]     INT              NOT NULL,                          -- denormalized
    [VersionHistoryId]  INT              NOT NULL,                          -- denormalized
    [RecipientUserId]   NVARCHAR(450)    NULL,                              -- nullable for synthetic recipients (none in v1; reserved)
    [RecipientEmail]    NVARCHAR(256)    NOT NULL,
    [Provider]          VARCHAR(32)      NOT NULL,                          -- 'MailtrapSmtp' | 'Mailgun' | 'NoOp'
    [ProviderMessageId] NVARCHAR(256)    NULL,
    [Status]            VARCHAR(24)      NOT NULL,                          -- Sent | Failed | DeadLetter | BlockedByAllowlist | Skipped
    [AttemptCount]      INT              NOT NULL CONSTRAINT [DF_NotificationDelivery_AttemptCount] DEFAULT (0),
    [LastError]         NVARCHAR(2000)   NULL,
    [SentAt]            DATETIME2(3)     NULL,                              -- nullable: NULL for Failed / BlockedByAllowlist / Skipped

    CONSTRAINT [PK_NotificationDelivery] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [FK_NotificationDelivery_Outbox]
        FOREIGN KEY ([OutboxId]) REFERENCES [dbo].[NotificationOutbox] ([Id]) ON DELETE CASCADE,
    CONSTRAINT [CK_NotificationDelivery_Status]
        CHECK ([Status] IN ('Sent', 'Failed', 'DeadLetter', 'BlockedByAllowlist', 'Skipped'))
);
GO

-- THE idempotency guard. (EventType, ApplicationId, VersionHistoryId, RecipientUserId) is unique across all NON-NULL recipient user ids.
-- A filtered unique index lets synthetic-recipient rows coexist without violating the dedup rule.
CREATE UNIQUE INDEX [UX_NotificationDelivery_DedupKey]
    ON [dbo].[NotificationDelivery] ([EventType], [ApplicationId], [VersionHistoryId], [RecipientUserId])
    WHERE [RecipientUserId] IS NOT NULL;
GO

-- Operational lookup: "show me the deliveries for this outbox row".
CREATE INDEX [IX_NotificationDelivery_OutboxId]
    ON [dbo].[NotificationDelivery] ([OutboxId]);
GO

-- Operational lookup: per-recipient history.
CREATE INDEX [IX_NotificationDelivery_RecipientEmail]
    ON [dbo].[NotificationDelivery] ([RecipientEmail], [SentAt] DESC);
GO
```

## EF Core mapping (Code-First, no migrations)

### `NotificationOutbox` entity

`src/FundingPlatform.Infrastructure/Notifications/Persistence/NotificationOutbox.cs`

```csharp
public class NotificationOutbox
{
    public long Id { get; private set; }
    public string EventType { get; private set; } = default!;
    public int ApplicationId { get; private set; }
    public int VersionHistoryId { get; private set; }
    public string PayloadJson { get; private set; } = default!;
    public DateTime CreatedAt { get; private set; }
    public string Status { get; private set; } = "Pending";  // backed by enum at consumer boundary
    public int AttemptCount { get; private set; }
    public string? LastError { get; private set; }
    public DateTime? NextAttemptAt { get; private set; }
    public byte[] RowVersion { get; private set; } = default!;

    // private parameterless ctor for EF.
    // factory: NotificationOutbox.Create(NotificationEvent, int, int, string).
    // behavior: ClaimForDispatch(), MarkDone(), MarkTransientFailure(error, nextAttemptAt), MarkDeadLetter(error).
}
```

Configuration: `NotificationOutboxConfiguration.cs` maps to `dbo.NotificationOutbox`, sets `RowVersion` as `IsConcurrencyToken().IsRowVersion()`.

### `NotificationDelivery` entity

`src/FundingPlatform.Infrastructure/Notifications/Persistence/NotificationDelivery.cs`

```csharp
public class NotificationDelivery
{
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

    // factory: NotificationDelivery.RecordSend(...), RecordFailure(...), RecordSkipped(...), RecordBlocked(...).
}
```

## Enums

### `NotificationEvent` (Domain)

`src/FundingPlatform.Domain/Notifications/NotificationEvent.cs`

```csharp
public enum NotificationEvent
{
    ApplicationSubmittedReviewer  = 1,  // "APPLICATION_SUBMITTED_REVIEWER"  — string code via EnumStringConverter
    ApplicationSubmittedApplicant = 2,
    ReturnedToApplicant           = 3,
    ResubmittedByApplicant        = 4,
    ApplicationApproved           = 5,
    ApplicationRejected           = 6,
}
```

Persisted as `VARCHAR(64)` upper-snake-case string per dacpac column type. EF mapping uses a string converter so DB rows remain operator-readable.

### Recipient bucket (Application)

`src/FundingPlatform.Application/Notifications/RecipientBucket.cs`

```csharp
public enum RecipientBucket
{
    Applicant = 1,
    Reviewer  = 2,
    Admin     = 3,
}
```

Bucket priority `Applicant > Reviewer > Admin` per FR-012 / §Recipient Rules. The resolver dedup logic keeps the lowest-ordinal entry per `(UserId, OutboxRow)` pair.

## Value Objects

### `NotificationRecipient`

`src/FundingPlatform.Application/Notifications/NotificationRecipient.cs`

```csharp
public sealed record NotificationRecipient(
    string? UserId,
    string Email,
    string DisplayName,
    RecipientBucket Bucket,
    string TemplateVariantKey);
```

Not persisted. Returned by `INotificationRecipientResolver.Resolve(NotificationOutbox row, CancellationToken ct)`.

### `NotificationPayload`

`src/FundingPlatform.Application/Notifications/NotificationPayload.cs`

```csharp
public sealed record NotificationPayload(
    int ApplicationId,
    string ApplicantUserId,
    string ApplicantDisplayName,
    IReadOnlyList<int> StageGroupIds,
    string? OutcomeCode);  // "Approved" | "Rejected" | null for non-terminal events
```

Serialized into `NotificationOutbox.PayloadJson`. The resolver reads this back; nothing else is needed because recipient identity is re-resolved at dispatch time (NOT snapshotted at outbox-write time — see EC-003 / EC-004).

## Indexes & access patterns

| Index | Used by |
|---|---|
| `IX_NotificationOutbox_Status_NextAttemptAt` | Worker poll: `SELECT TOP @batch * FROM NotificationOutbox WHERE Status = 'Pending' OR (Status = 'Dispatching' AND NextAttemptAt <= SYSUTCDATETIME()) ORDER BY CreatedAt` |
| `IX_NotificationOutbox_ApplicationId` | Operational dashboards / debug queries by application id |
| `UX_NotificationDelivery_DedupKey` | Worker pre-send check (FR-020). Concurrent-double-send via two workers becomes a UNIQUE-violation rollback rather than a duplicate email. |
| `IX_NotificationDelivery_OutboxId` | "Show me all deliveries for outbox row X" admin view |
| `IX_NotificationDelivery_RecipientEmail` | "Show me everything we've sent this address" support query |

## Constraints

- `NotificationOutbox.Status` CHECK ensures only the four declared statuses persist.
- `NotificationDelivery.Status` CHECK ensures only the five declared statuses persist.
- Outbox `ApplicationId` FK uses `ON DELETE CASCADE` so EC-005 (application hard-deleted) cleans up the outbox row.
- Outbox `VersionHistoryId` FK uses `ON DELETE NO ACTION` — version history is intended to outlive its parent in audit scenarios; a hard-deleted application clears VersionHistory via spec 002's own cascade. (Verified compatible at planning time.)
- Delivery `OutboxId` FK uses `ON DELETE CASCADE` so cleaning up an outbox row cleans its deliveries.

## Retention (per OQ-008, Clarified 2026-05-12)

| Status set | Retention |
|---|---|
| `NotificationOutbox.Status = 'Done'` | 90 days |
| `NotificationOutbox.Status = 'DeadLetter'` | 1 year |
| `NotificationDelivery.Status IN ('Sent','BlockedByAllowlist','Skipped')` | 90 days |
| `NotificationDelivery.Status IN ('Failed','DeadLetter')` | 1 year |

A nightly cleanup job is **out of v1 scope**. Rows accumulate. A future operational task introduces a scheduled cleanup; the indexes above are sized to remain healthy under one year of accumulation.

## Validation

- `EventType` not null, max 64 chars, matches enum.
- `RecipientEmail` not null, max 256 chars, structurally email-shaped (validation at outbox writer / resolver, not DB).
- `ProviderMessageId` ≤ 256 chars when present.
- `LastError` ≤ 2000 chars (truncated by writer if longer).
- `PayloadJson` ≤ 4 KB target (NVARCHAR(MAX) physically allows more; logical cap is enforced at outbox writer).
