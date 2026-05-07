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
    -- Spec 015 note: declared as CHAR(3) NULL here so the dacpac diff matches the
    -- post-deployment ALTER (CHAR(3) NOT NULL with FK to dbo.Currencies(Code)).
    -- On a fresh deploy: schema creates the nullable column; the post-deploy
    -- backfills NULL → 'CRC', tightens to NOT NULL, and adds the FK. Both halves
    -- agree on CHAR(3) so SQL Server's FK constraint check (Msg 1778) is happy.
    [Currency]          CHAR(3)       NULL,
    [CreatedAt]         DATETIME2     NOT NULL CONSTRAINT [DF_Quotations_CreatedAt] DEFAULT (GETUTCDATE()),

    -- Spec 015: multi-currency snapshot fields. CRC quotes leave Snapshot* NULL
    -- and copy Price into ConvertedCrcAmount; non-CRC quotes embed the rate that
    -- was applied at save time so the converted CRC value is stable across
    -- subsequent rate changes (FR-013, FR-016). The two CHECK constraints that
    -- enforce the snapshot/legacy invariants are added in PostDeployment/SeedData.sql
    -- AFTER legacy rows are stamped or flagged — adding them here would fail the
    -- upgrade because pre-existing non-CRC rows do not yet carry a snapshot.
    [ConvertedCrcAmount]      DECIMAL(18, 2)   NULL,
    [SnapshotRateValue]       DECIMAL(18, 6)   NULL,
    [SnapshotRateType]        TINYINT          NULL,
    [SnapshotEffectiveAtUtc]  DATETIME2(3)     NULL,
    [SnapshotRateId]          UNIQUEIDENTIFIER NULL,
    -- Pre-existing non-CRC rows lacking a snapshot are flagged here by the
    -- post-deploy migration; admins clear the flag via the legacy-attach UI (US6).
    [LegacyNeedsReview]       BIT              NOT NULL CONSTRAINT [DF_Quotations_LegacyNeedsReview] DEFAULT (0),

    CONSTRAINT [PK_Quotations] PRIMARY KEY CLUSTERED ([Id]),
    -- One quotation per (item, supplier) — branch is contact metadata, not a
    -- separate quote source (research.md R1).
    CONSTRAINT [UX_Quotations_ItemId_SupplierId] UNIQUE ([ItemId], [SupplierId]),
    CONSTRAINT [FK_Quotations_Items] FOREIGN KEY ([ItemId]) REFERENCES [dbo].[Items] ([Id]) ON DELETE CASCADE,
    CONSTRAINT [FK_Quotations_Suppliers] FOREIGN KEY ([SupplierId]) REFERENCES [dbo].[Suppliers] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_Quotations_SupplierBranches] FOREIGN KEY ([SupplierBranchId]) REFERENCES [dbo].[SupplierBranches] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_Quotations_Documents] FOREIGN KEY ([DocumentId]) REFERENCES [dbo].[Documents] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_Quotations_ExchangeRates] FOREIGN KEY ([SnapshotRateId]) REFERENCES [dbo].[ExchangeRates] ([Id]) ON DELETE NO ACTION
);
GO

CREATE INDEX [IX_Quotations_SupplierBranchId] ON [dbo].[Quotations] ([SupplierBranchId]);
GO

-- Spec 015: admin "needs review" queue (US6). Filtered to keep the index narrow.
CREATE INDEX [IX_Quotations_LegacyNeedsReview]
    ON [dbo].[Quotations] ([LegacyNeedsReview])
    WHERE [LegacyNeedsReview] = 1;
GO

-- Spec 015: support FK lookups for "which quotes used rate R" audit queries.
CREATE INDEX [IX_Quotations_SnapshotRateId]
    ON [dbo].[Quotations] ([SnapshotRateId]);
GO
