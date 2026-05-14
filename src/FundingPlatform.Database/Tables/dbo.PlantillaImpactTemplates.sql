CREATE TABLE [dbo].[PlantillaImpactTemplates]
(
    [PlantillaId]       INT             NOT NULL,
    [ImpactTemplateId]  INT             NOT NULL,
    [CreatedAt]         DATETIME2(0)    NOT NULL CONSTRAINT [DF_PlantillaImpactTemplates_CreatedAt] DEFAULT (SYSUTCDATETIME()),

    CONSTRAINT [PK_PlantillaImpactTemplates] PRIMARY KEY CLUSTERED ([PlantillaId], [ImpactTemplateId]),
    CONSTRAINT [FK_PlantillaImpactTemplates_Plantillas]
        FOREIGN KEY ([PlantillaId]) REFERENCES [dbo].[Plantillas] ([Id]) ON DELETE CASCADE,
    CONSTRAINT [FK_PlantillaImpactTemplates_ImpactTemplates]
        FOREIGN KEY ([ImpactTemplateId]) REFERENCES [dbo].[ImpactTemplates] ([Id]) ON DELETE NO ACTION
);
GO

-- Reverse lookup: enumerate Plantillas that reference a given ImpactTemplate
-- (used by Plantilla.AssignTo guard and admin queries).
CREATE NONCLUSTERED INDEX [IX_PlantillaImpactTemplates_ImpactTemplateId]
    ON [dbo].[PlantillaImpactTemplates] ([ImpactTemplateId], [PlantillaId]);
GO
