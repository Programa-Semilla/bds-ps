CREATE TABLE [dbo].[AdminAuditEvents]
(
    [Id]            BIGINT          IDENTITY(1,1) NOT NULL,
    [OccurredAt]    DATETIMEOFFSET  NOT NULL CONSTRAINT [DF_AdminAuditEvents_OccurredAt] DEFAULT (SYSUTCDATETIME()),
    [ActorUserId]   NVARCHAR(450)   NOT NULL,
    [Action]        NVARCHAR(64)    NOT NULL,
    [TargetType]    NVARCHAR(64)    NOT NULL,
    [TargetId]      NVARCHAR(64)    NOT NULL,
    [PayloadJson]   NVARCHAR(MAX)   NULL,

    CONSTRAINT [PK_AdminAuditEvents] PRIMARY KEY CLUSTERED ([Id]),
    -- NFR-005 — actor must always resolve to a user. NO ACTION on delete so
    -- audit rows survive user deletion.
    CONSTRAINT [FK_AdminAuditEvents_AspNetUsers]
        FOREIGN KEY ([ActorUserId]) REFERENCES [dbo].[AspNetUsers] ([Id]) ON DELETE NO ACTION
);
GO

CREATE NONCLUSTERED INDEX [IX_AdminAuditEvents_OccurredAt]
    ON [dbo].[AdminAuditEvents] ([OccurredAt] DESC);
GO

CREATE NONCLUSTERED INDEX [IX_AdminAuditEvents_Target]
    ON [dbo].[AdminAuditEvents] ([TargetType], [TargetId], [OccurredAt] DESC);
GO
