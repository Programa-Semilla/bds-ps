CREATE TABLE [dbo].[Items]
(
    [Id]                          INT            IDENTITY(1,1) NOT NULL,
    [ApplicationId]               INT            NOT NULL,
    [LineCode]                    NVARCHAR(16)   NULL,
    [ProductName]                 NVARCHAR(500)  NOT NULL,
    [CategoryId]                  INT            NOT NULL,
    [TechnicalSpecifications]     NVARCHAR(MAX)  NOT NULL,
    [ReviewStatus]                INT            NOT NULL CONSTRAINT [DF_Items_ReviewStatus] DEFAULT (0),
    [ReviewComment]               NVARCHAR(2000) NULL,
    [SelectedSupplierId]          INT            NULL,
    [IsNotTechnicallyEquivalent]  BIT            NOT NULL CONSTRAINT [DF_Items_IsNotTechnicallyEquivalent] DEFAULT (0),
    [CreatedAt]                   DATETIME2      NOT NULL CONSTRAINT [DF_Items_CreatedAt] DEFAULT (GETUTCDATE()),
    [UpdatedAt]                   DATETIME2      NOT NULL,

    CONSTRAINT [PK_Items] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [FK_Items_Applications] FOREIGN KEY ([ApplicationId]) REFERENCES [dbo].[Applications] ([Id]) ON DELETE CASCADE,
    CONSTRAINT [FK_Items_Categories] FOREIGN KEY ([CategoryId]) REFERENCES [dbo].[Categories] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_Items_Suppliers_SelectedSupplierId] FOREIGN KEY ([SelectedSupplierId]) REFERENCES [dbo].[Suppliers] ([Id])
);
GO

CREATE NONCLUSTERED INDEX [IX_Items_ApplicationId]
    ON [dbo].[Items] ([ApplicationId]);
GO

CREATE NONCLUSTERED INDEX [IX_Items_CategoryId]
    ON [dbo].[Items] ([CategoryId]);
GO

-- Spec 018 / FR-013 — line code is unique within a single Application; the filtered
-- predicate excludes unassigned (NULL) rows so applicant-side draft items can
-- coexist before any reviewer has touched them.
CREATE UNIQUE INDEX [UX_Items_Application_LineCode]
    ON [dbo].[Items] ([ApplicationId], [LineCode])
    WHERE [LineCode] IS NOT NULL;
GO
