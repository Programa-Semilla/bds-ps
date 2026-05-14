CREATE TABLE [dbo].[Processes]
(
    [Id]                     INT            IDENTITY(1,1) NOT NULL,
    [Name]                   NVARCHAR(120)  NOT NULL,
    [Status]                 TINYINT        NOT NULL CONSTRAINT [DF_Processes_Status] DEFAULT (0),
    [SolicitudWindowDays]    INT            NULL,
    [RevisionWindowDays]     INT            NULL,
    [FacturacionWindowDays]  INT            NULL,
    [CreatedAt]              DATETIME2(0)   NOT NULL CONSTRAINT [DF_Processes_CreatedAt] DEFAULT (SYSUTCDATETIME()),
    [ClosedAt]               DATETIME2(0)   NULL,
    [RowVersion]             ROWVERSION     NOT NULL,

    CONSTRAINT [PK_Processes] PRIMARY KEY CLUSTERED ([Id])
);
GO

-- Spec 021 / data-model.md — Process.Name uniqueness across the catalog.
-- Reuse-across-closed-cycles is enforced at the application layer (Status filter);
-- the unique index here is the authoritative gate against duplicate names submitted
-- concurrently by two admins.
CREATE UNIQUE NONCLUSTERED INDEX [UX_Processes_Name]
    ON [dbo].[Processes] ([Name]);
GO
