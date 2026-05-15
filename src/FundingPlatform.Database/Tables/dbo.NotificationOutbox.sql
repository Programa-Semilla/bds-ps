-- Spec 021 / T002 / FR-002 / NFR-005 — Transactional outbox for outbound email
-- notifications. Written in the SAME EF SaveChangesAsync transaction as the
-- workflow state change that triggered the notification, then dispatched async
-- by EmailDispatchWorker (BackgroundService).
CREATE TABLE [dbo].[NotificationOutbox]
(
    [Id]                BIGINT           IDENTITY(1,1) NOT NULL,
    [EventType]         VARCHAR(64)      NOT NULL,                          -- see NotificationEvent enum (upper-snake-case storage form)
    [ApplicationId]     INT              NOT NULL,
    [VersionHistoryId]  INT              NOT NULL,                          -- FK to VersionHistory.Id; identifies the workflow row that triggered this notification
    [PayloadJson]       NVARCHAR(MAX)    NOT NULL,                          -- denormalized snapshot used only as a hint; recipient resolution re-runs at dispatch time (EC-003)
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
CREATE NONCLUSTERED INDEX [IX_NotificationOutbox_Status_NextAttemptAt]
    ON [dbo].[NotificationOutbox] ([Status], [NextAttemptAt])
    INCLUDE ([Id], [EventType], [ApplicationId], [VersionHistoryId], [AttemptCount]);
GO

-- Operational lookup: "give me everything for application X".
CREATE NONCLUSTERED INDEX [IX_NotificationOutbox_ApplicationId]
    ON [dbo].[NotificationOutbox] ([ApplicationId], [CreatedAt] DESC);
GO
