CREATE TABLE [dbo].[ImpactParameterValues]
(
    [Id]                        INT           IDENTITY(1,1) NOT NULL,
    -- Spec 021 / data-model.md — re-parented from the legacy [ImpactId] (the
    -- standalone dbo.Impacts table was dropped, FR-005 + NFR-001 "no production
    -- data"). Read paths now project Impact off Application directly via the
    -- Impact value object (Domain.ValueObjects.Impact).
    [ApplicationId]             INT           NOT NULL,
    [ImpactTemplateParameterId] INT           NOT NULL,
    [Value]                     NVARCHAR(MAX) NULL,

    CONSTRAINT [PK_ImpactParameterValues] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [UX_ImpactParamValues_AppId_ParamId] UNIQUE ([ApplicationId], [ImpactTemplateParameterId]),
    CONSTRAINT [FK_ImpactParamValues_Applications]
        FOREIGN KEY ([ApplicationId]) REFERENCES [dbo].[Applications] ([Id]) ON DELETE CASCADE,
    CONSTRAINT [FK_ImpactParamValues_ImpactTemplateParams]
        FOREIGN KEY ([ImpactTemplateParameterId]) REFERENCES [dbo].[ImpactTemplateParameters] ([Id]) ON DELETE NO ACTION
);
GO

CREATE NONCLUSTERED INDEX [IX_ImpactParameterValues_ApplicationId]
    ON [dbo].[ImpactParameterValues] ([ApplicationId]);
GO
