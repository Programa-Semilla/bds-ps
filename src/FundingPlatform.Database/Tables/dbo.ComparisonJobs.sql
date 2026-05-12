-- Spec 020 / data-model.md — queued/in-flight comparison generation request.
-- ApplicationItemId is INT (matches dbo.Items.Id). Id remains UNIQUEIDENTIFIER
-- so the worker can pre-allocate identifiers and write rows in a single insert.
CREATE TABLE [dbo].[ComparisonJobs]
(
    [Id]                     UNIQUEIDENTIFIER  NOT NULL,
    [ApplicationItemId]      INT               NOT NULL,
    [RequestedByUserId]      NVARCHAR(450)     NOT NULL,
    [Status]                 NVARCHAR(16)      NOT NULL,  -- Pending|Running|Completed|Failed
    [BypassedRateLimit]      BIT               NOT NULL,
    [BypassedTokenCap]       BIT               NOT NULL,
    [LastStatusChangeAt]     DATETIMEOFFSET    NOT NULL,
    [ResultingArtifactId]    INT               NULL,
    [FailureReason]          NVARCHAR(128)     NULL,
    [StartedAt]              DATETIMEOFFSET    NULL,
    [FinishedAt]             DATETIMEOFFSET    NULL,

    CONSTRAINT [PK_ComparisonJobs]
        PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [FK_ComparisonJobs_Items]
        FOREIGN KEY ([ApplicationItemId])
        REFERENCES [dbo].[Items]([Id])
        ON DELETE CASCADE
);
GO
CREATE INDEX [IX_ComparisonJobs_Status_LastStatusChangeAt]
    ON [dbo].[ComparisonJobs]([Status], [LastStatusChangeAt]);
GO
CREATE INDEX [IX_ComparisonJobs_ApplicationItemId_Status]
    ON [dbo].[ComparisonJobs]([ApplicationItemId], [Status]);
GO
