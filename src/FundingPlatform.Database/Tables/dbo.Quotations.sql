CREATE TABLE [dbo].[Quotations]
(
    [Id]                INT           IDENTITY(1,1) NOT NULL,
    [ItemId]            INT           NOT NULL,
    [SupplierId]        INT           NOT NULL,

    -- Spec 013: branch reference. Default 0 lets dacpac add the column NOT NULL on
    -- existing rows; the post-deployment migration backfills real branch IDs and
    -- asserts no rows remain at 0/NULL before completing the transaction.
    [SupplierBranchId]  INT           NOT NULL CONSTRAINT [DF_Quotations_SupplierBranchId] DEFAULT (0),

    [Price]             DECIMAL(18,2) NOT NULL,
    [ValidUntil]        DATE          NOT NULL,
    [DocumentId]        INT           NOT NULL,
    [Currency]          NVARCHAR(3)   NULL,
    [CreatedAt]         DATETIME2     NOT NULL CONSTRAINT [DF_Quotations_CreatedAt] DEFAULT (GETUTCDATE()),

    CONSTRAINT [PK_Quotations] PRIMARY KEY CLUSTERED ([Id]),
    -- One quotation per (item, supplier) — branch is contact metadata, not a
    -- separate quote source (research.md R1).
    CONSTRAINT [UX_Quotations_ItemId_SupplierId] UNIQUE ([ItemId], [SupplierId]),
    CONSTRAINT [FK_Quotations_Items] FOREIGN KEY ([ItemId]) REFERENCES [dbo].[Items] ([Id]) ON DELETE CASCADE,
    CONSTRAINT [FK_Quotations_Suppliers] FOREIGN KEY ([SupplierId]) REFERENCES [dbo].[Suppliers] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_Quotations_SupplierBranches] FOREIGN KEY ([SupplierBranchId]) REFERENCES [dbo].[SupplierBranches] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_Quotations_Documents] FOREIGN KEY ([DocumentId]) REFERENCES [dbo].[Documents] ([Id]) ON DELETE NO ACTION
);
GO

CREATE INDEX [IX_Quotations_SupplierBranchId] ON [dbo].[Quotations] ([SupplierBranchId]);
GO
