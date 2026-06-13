CREATE TABLE [dbo].[ImpactParameterValues]
(
    [Id]                        INT           IDENTITY(1,1) NOT NULL,
    -- Spec 035 / data-model.md (D2) — re-keyed from [ApplicationId] to [ItemId].
    -- Impact relocated from Application down to the line item, so parameter values
    -- are now scoped per Item. Greenfield flow → destructive re-key, no backfill.
    [ItemId]                    INT           NOT NULL,
    [ImpactTemplateParameterId] INT           NOT NULL,
    [Value]                     NVARCHAR(MAX) NULL,

    CONSTRAINT [PK_ImpactParameterValues] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [UX_ImpactParamValues_ItemId_ParamId] UNIQUE ([ItemId], [ImpactTemplateParameterId]),
    CONSTRAINT [FK_ImpactParamValues_Items]
        FOREIGN KEY ([ItemId]) REFERENCES [dbo].[Items] ([Id]) ON DELETE CASCADE,
    CONSTRAINT [FK_ImpactParamValues_ImpactTemplateParams]
        FOREIGN KEY ([ImpactTemplateParameterId]) REFERENCES [dbo].[ImpactTemplateParameters] ([Id]) ON DELETE NO ACTION
);
GO

CREATE NONCLUSTERED INDEX [IX_ImpactParameterValues_ItemId]
    ON [dbo].[ImpactParameterValues] ([ItemId]);
GO
