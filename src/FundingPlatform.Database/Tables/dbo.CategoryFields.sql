-- Spec 035 / data-model.md — admin-configured field set owned 1:1 by a Category.
-- Mirrors dbo.ImpactTemplateParameters (the proven impact-template pattern). The
-- applicant fills these per line item; values land in dbo.CategoryFieldValues.
CREATE TABLE [dbo].[CategoryFields]
(
    [Id]           INT            IDENTITY(1,1) NOT NULL,
    [CategoryId]   INT            NOT NULL,
    [Name]         NVARCHAR(200)  NOT NULL,
    [DisplayLabel] NVARCHAR(300)  NOT NULL,
    [DataType]     INT            NOT NULL,
    [IsRequired]   BIT            NOT NULL CONSTRAINT [DF_CategoryFields_IsRequired] DEFAULT (1),
    [SortOrder]    INT            NOT NULL CONSTRAINT [DF_CategoryFields_SortOrder] DEFAULT (0),

    CONSTRAINT [PK_CategoryFields] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [FK_CategoryFields_Categories]
        FOREIGN KEY ([CategoryId]) REFERENCES [dbo].[Categories] ([Id]) ON DELETE CASCADE
);
GO

CREATE NONCLUSTERED INDEX [IX_CategoryFields_CategoryId]
    ON [dbo].[CategoryFields] ([CategoryId]);
GO
