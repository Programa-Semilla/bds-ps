CREATE TABLE [dbo].[ApplicationImpacts]
(
    [Id]               INT IDENTITY (1, 1) NOT NULL,
    -- Spec 035 (evolved 2026-06-16, data-model.md D13) — impact data collection lives
    -- at the application level, one-or-more impacts per application. Each row is one
    -- declared impact: a chosen ImpactTemplate. Its entered values live in
    -- ImpactParameterValues, re-keyed to ApplicationImpactId.
    [ApplicationId]    INT NOT NULL,
    [ImpactTemplateId] INT NOT NULL,

    CONSTRAINT [PK_ApplicationImpacts] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [FK_ApplicationImpacts_Applications]
        FOREIGN KEY ([ApplicationId]) REFERENCES [dbo].[Applications] ([Id]) ON DELETE CASCADE,
    CONSTRAINT [FK_ApplicationImpacts_ImpactTemplates]
        FOREIGN KEY ([ImpactTemplateId]) REFERENCES [dbo].[ImpactTemplates] ([Id]) ON DELETE NO ACTION,
    -- The same impact template cannot be declared twice on one application.
    CONSTRAINT [UX_ApplicationImpacts_AppId_TemplateId] UNIQUE ([ApplicationId], [ImpactTemplateId])
);
GO

CREATE NONCLUSTERED INDEX [IX_ApplicationImpacts_ApplicationId]
    ON [dbo].[ApplicationImpacts] ([ApplicationId]);
GO
