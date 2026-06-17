CREATE TABLE [dbo].[ImpactParameterValues]
(
    [Id]                        INT           IDENTITY(1,1) NOT NULL,
    -- Spec 035 (evolved 2026-06-16, data-model.md D13) — re-keyed to [ApplicationImpactId].
    -- Impact data collection lives at the application level (one-or-more declared impacts),
    -- so parameter values are scoped per ApplicationImpact. (Pre-035 keyed by ApplicationId;
    -- the superseded per-item 035 design keyed by ItemId.) Greenfield → destructive re-key.
    [ApplicationImpactId]       INT           NOT NULL,
    [ImpactTemplateParameterId] INT           NOT NULL,
    [Value]                     NVARCHAR(MAX) NULL,

    CONSTRAINT [PK_ImpactParameterValues] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [UX_ImpactParamValues_AppImpactId_ParamId] UNIQUE ([ApplicationImpactId], [ImpactTemplateParameterId]),
    CONSTRAINT [FK_ImpactParamValues_ApplicationImpacts]
        FOREIGN KEY ([ApplicationImpactId]) REFERENCES [dbo].[ApplicationImpacts] ([Id]) ON DELETE CASCADE,
    CONSTRAINT [FK_ImpactParamValues_ImpactTemplateParams]
        FOREIGN KEY ([ImpactTemplateParameterId]) REFERENCES [dbo].[ImpactTemplateParameters] ([Id]) ON DELETE NO ACTION
);
GO

CREATE NONCLUSTERED INDEX [IX_ImpactParameterValues_ApplicationImpactId]
    ON [dbo].[ImpactParameterValues] ([ApplicationImpactId]);
GO
