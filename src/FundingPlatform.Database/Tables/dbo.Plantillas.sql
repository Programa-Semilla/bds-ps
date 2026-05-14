CREATE TABLE [dbo].[Plantillas]
(
    [Id]                         INT            IDENTITY(1,1) NOT NULL,
    [Name]                       NVARCHAR(120)  NOT NULL,
    [MinimumQuotationsPerItem]   INT            NOT NULL CONSTRAINT [DF_Plantillas_MinimumQuotationsPerItem] DEFAULT (3),
    [RequiredFieldFlags]         BIGINT         NOT NULL CONSTRAINT [DF_Plantillas_RequiredFieldFlags] DEFAULT (0),
    [IsArchived]                 BIT            NOT NULL CONSTRAINT [DF_Plantillas_IsArchived] DEFAULT (0),
    [CreatedAt]                  DATETIME2(0)   NOT NULL CONSTRAINT [DF_Plantillas_CreatedAt] DEFAULT (SYSUTCDATETIME()),
    [RowVersion]                 ROWVERSION     NOT NULL,

    CONSTRAINT [PK_Plantillas] PRIMARY KEY CLUSTERED ([Id])
);
GO

-- Spec 021 / data-model.md — Plantilla.Name is a human-friendly identifier
-- (e.g. "PlantillaMVP-v1"). Application layer scopes uniqueness to non-archived
-- rows; the index here is a soft guard.
CREATE UNIQUE NONCLUSTERED INDEX [UX_Plantillas_Name]
    ON [dbo].[Plantillas] ([Name]);
GO
