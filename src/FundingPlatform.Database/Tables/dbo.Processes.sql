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
    [SolicitudWindowDays]    INT            NULL,
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

-- Spec 021 / data-model.md — Process.Name uniqueness across the catalog.
-- Reuse-across-closed-cycles is enforced at the application layer (Status filter);
-- the unique index here is the authoritative gate against duplicate names submitted
-- concurrently by two admins.
CREATE UNIQUE NONCLUSTERED INDEX [UX_Processes_Name]
    ON [dbo].[Processes] ([Name]);
GO
