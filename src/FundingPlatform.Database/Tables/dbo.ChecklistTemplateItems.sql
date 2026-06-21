-- Spec 040 / D5 / D13 — a single text-only verification line owned by a
-- ChecklistTemplate. Cascade within the template aggregate (parent template owns its
-- items). See specs/040-auditor-workflow-stage/data-model.md.
CREATE TABLE [dbo].[ChecklistTemplateItems]
(
    [Id]                  INT            IDENTITY(1,1) NOT NULL,
    [ChecklistTemplateId] INT            NOT NULL,
    [Text]                NVARCHAR(500)  NOT NULL,
    [DisplayOrder]        INT            NOT NULL CONSTRAINT [DF_ChecklistTemplateItems_DisplayOrder] DEFAULT (0),
    [IsRequired]          BIT            NOT NULL CONSTRAINT [DF_ChecklistTemplateItems_IsRequired] DEFAULT (1),
    [IsActive]            BIT            NOT NULL CONSTRAINT [DF_ChecklistTemplateItems_IsActive] DEFAULT (1),

    CONSTRAINT [PK_ChecklistTemplateItems] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [FK_ChecklistTemplateItems_ChecklistTemplates]
        FOREIGN KEY ([ChecklistTemplateId]) REFERENCES [dbo].[ChecklistTemplates]([Id]) ON DELETE CASCADE
);
GO

CREATE NONCLUSTERED INDEX [IX_ChecklistTemplateItems_ChecklistTemplateId]
    ON [dbo].[ChecklistTemplateItems] ([ChecklistTemplateId]);
GO
