CREATE TABLE [dbo].[Processes]
(
    [Id]                     INT            IDENTITY(1,1) NOT NULL,
    [Name]                   NVARCHAR(120)  NOT NULL,
    [Status]                 TINYINT        NOT NULL CONSTRAINT [DF_Processes_Status] DEFAULT (0),
    -- Spec 029 / FR-002 — every Process belongs to exactly one Fund (Fondo).
    -- Required FK; pre-production so no nullable/backfill phase (research D4).
    -- The post-deploy seed (00_SeedFunds) creates the seed Fund before the
    -- "Migración inicial" Process is inserted with a FundId.
    [FundId]                 INT            NOT NULL,
    [SolicitudWindowDays]    INT            NULL,
    [RevisionWindowDays]     INT            NULL,
    [FacturacionWindowDays]  INT            NULL,
    [CreatedAt]              DATETIMEOFFSET(0)   NOT NULL CONSTRAINT [DF_Processes_CreatedAt] DEFAULT (SYSUTCDATETIME()),
    [ClosedAt]               DATETIMEOFFSET(0)   NULL,
    [RowVersion]             ROWVERSION     NOT NULL,

    CONSTRAINT [PK_Processes] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [FK_Processes_Funds] FOREIGN KEY ([FundId]) REFERENCES [dbo].[Funds] ([Id]) ON DELETE NO ACTION
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
