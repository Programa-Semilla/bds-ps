-- Spec 035 / data-model.md — applicant-entered value for one CategoryField on one
-- Item (EAV). Mirrors dbo.ImpactParameterValues but keyed by ItemId (a category is
-- chosen per line item). Value stored as string; type coercion is app-layer.
CREATE TABLE [dbo].[CategoryFieldValues]
(
    [Id]              INT           IDENTITY(1,1) NOT NULL,
    [ItemId]          INT           NOT NULL,
    [CategoryFieldId] INT           NOT NULL,
    [Value]           NVARCHAR(MAX) NULL,

    CONSTRAINT [PK_CategoryFieldValues] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [UX_CategoryFieldValues_ItemId_FieldId] UNIQUE ([ItemId], [CategoryFieldId]),
    CONSTRAINT [FK_CategoryFieldValues_Items]
        FOREIGN KEY ([ItemId]) REFERENCES [dbo].[Items] ([Id]) ON DELETE CASCADE,
    CONSTRAINT [FK_CategoryFieldValues_CategoryFields]
        FOREIGN KEY ([CategoryFieldId]) REFERENCES [dbo].[CategoryFields] ([Id]) ON DELETE NO ACTION
);
GO

CREATE NONCLUSTERED INDEX [IX_CategoryFieldValues_ItemId]
    ON [dbo].[CategoryFieldValues] ([ItemId]);
GO
