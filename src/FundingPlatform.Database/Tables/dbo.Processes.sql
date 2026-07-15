CREATE TABLE [dbo].[Processes]
(
    [Id]                     INT            IDENTITY(1,1) NOT NULL,
    [Name]                   NVARCHAR(120)  NOT NULL,
    [Status]                 TINYINT        NOT NULL CONSTRAINT [DF_Processes_Status] DEFAULT (0),
    -- Spec 029 / FR-002 — every Process belongs to exactly one Fund (Fondo).
    -- Migration-safe: a DEFAULT(0) placeholder lets the column be added to an
    -- already-populated table (existing dev/staging DBs). The post-deploy step
    -- 05_Fund029Anchors backfills FundId to the seed Fund and then adds
    -- FK_Processes_Funds — mirroring the Groups.ProcessId pattern. Declaring the
    -- column NOT NULL with an inline FK (no default) would fail the publish on
    -- any table that already has rows, rolling back the whole deployment.
    [FundId]                 INT            NOT NULL CONSTRAINT [DF_Processes_FundId] DEFAULT (0),
    -- Spec 044 — SolicitudWindowDays dropped; reception windows (dbo.ProcessEvents)
    -- replace the Solicitud duration submission gate. Dropped on populated DBs by
    -- PostDeployment/09_DropSolicitudWindowDays.sql.
    [RevisionWindowDays]     INT            NULL,
    [FacturacionWindowDays]  INT            NULL,
    [CreatedAt]              DATETIMEOFFSET(0)   NOT NULL CONSTRAINT [DF_Processes_CreatedAt] DEFAULT (SYSUTCDATETIME()),
    [ClosedAt]               DATETIMEOFFSET(0)   NULL,
    [RowVersion]             ROWVERSION     NOT NULL,

    CONSTRAINT [PK_Processes] PRIMARY KEY CLUSTERED ([Id])
    -- FK_Processes_Funds is added in post-deploy (05_Fund029Anchors) after backfill.
);
GO

CREATE NONCLUSTERED INDEX [IX_Processes_FundId]
    ON [dbo].[Processes] ([FundId]);
GO

-- Process.Name uniqueness is scoped PER FUND (not global): a process name must be
-- unique among the processes of its own Fund, but the same name may be reused by a
-- process in a different Fund. The composite unique index (FundId, Name) is the
-- authoritative gate against duplicate names submitted concurrently by two admins.
CREATE UNIQUE NONCLUSTERED INDEX [UX_Processes_FundId_Name]
    ON [dbo].[Processes] ([FundId], [Name]);
GO
