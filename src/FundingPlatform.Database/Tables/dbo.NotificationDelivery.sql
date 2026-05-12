-- Spec 021 / T003 / FR-020 / FR-028 / NFR-005 — Per-recipient delivery audit.
-- One row per (outbox row, recipient) pair. The filtered unique index on
-- (EventType, ApplicationId, VersionHistoryId, RecipientUserId) is the
-- idempotency guard: a concurrent double-send becomes a UNIQUE-violation
-- rollback rather than a duplicate email.
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

-- THE idempotency guard. Filtered to NON-NULL RecipientUserId so synthetic
-- recipient rows (reserved for the future) can coexist without violating dedup.
CREATE UNIQUE NONCLUSTERED INDEX [UX_NotificationDelivery_DedupKey]
    ON [dbo].[NotificationDelivery] ([EventType], [ApplicationId], [VersionHistoryId], [RecipientUserId])
    WHERE [RecipientUserId] IS NOT NULL;
GO

-- Operational lookup: "show me the deliveries for this outbox row".
CREATE NONCLUSTERED INDEX [IX_NotificationDelivery_OutboxId]
    ON [dbo].[NotificationDelivery] ([OutboxId]);
GO

-- Operational lookup: per-recipient history.
CREATE NONCLUSTERED INDEX [IX_NotificationDelivery_RecipientEmail]
    ON [dbo].[NotificationDelivery] ([RecipientEmail], [SentAt] DESC);
GO
