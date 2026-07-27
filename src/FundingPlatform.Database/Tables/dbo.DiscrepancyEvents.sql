-- Spec 048 — the append-only, immutable lifecycle-history chain for a dbo.Discrepancies row: one
-- entry per transition (detected/assigned/under-correction/resolved/waived/reopened). Never updated
-- or deleted; CASCADE-deleted with its parent (single-ownership child). FromState/ToState are TINYINT
-- enums (EF HasConversion<byte>()). ActorUserId is the real system-sentinel id for auto transitions
-- (spec-043 lesson — never the literal 'system'). See specs/048-full-reconciliation-engine/data-model.md.
CREATE TABLE [dbo].[DiscrepancyEvents]
(
    [Id]            INT               IDENTITY(1,1) NOT NULL,
    [DiscrepancyId] INT               NOT NULL,
    [OccurredAt]    DATETIMEOFFSET(0)  NOT NULL CONSTRAINT [DF_DiscrepancyEvents_OccurredAt] DEFAULT (SYSUTCDATETIME()),
    [ActorUserId]   NVARCHAR(450)     NOT NULL,
    [FromState]     TINYINT           NOT NULL,
    [ToState]       TINYINT           NOT NULL,
    [Kind]          NVARCHAR(30)      NOT NULL,
    [Reason]        NVARCHAR(500)     NULL,
    [Note]          NVARCHAR(500)     NULL,

    CONSTRAINT [PK_DiscrepancyEvents] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [FK_DiscrepancyEvents_Discrepancies]
        FOREIGN KEY ([DiscrepancyId]) REFERENCES [dbo].[Discrepancies]([Id]) ON DELETE CASCADE,
    CONSTRAINT [FK_DiscrepancyEvents_AspNetUsers]
        FOREIGN KEY ([ActorUserId]) REFERENCES [dbo].[AspNetUsers]([Id]) ON DELETE NO ACTION
);
GO

CREATE NONCLUSTERED INDEX [IX_DiscrepancyEvents_Discrepancy]
    ON [dbo].[DiscrepancyEvents] ([DiscrepancyId], [OccurredAt]);
GO
