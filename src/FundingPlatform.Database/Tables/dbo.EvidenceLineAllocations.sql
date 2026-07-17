-- Spec 047 — the M:N join realizing per-line evidence attribution: a portion of an Evidence
-- document allocated to one budget-line (Item). Owned by the Evidence (replace-all set on
-- Allocate). Follows the dbo.DisbursementLineAllocations / dbo.ItemImpacts topology exactly: two
-- FK paths reach [Applications] (via Evidence and via Items), so only the Evidence path is CASCADE
-- and the Items path is NO ACTION — otherwise the dacpac publish fails with a multiple-cascade-path
-- error (the spec-029/035/046 lesson). See specs/047-evidence-graph-required-docs/data-model.md.
CREATE TABLE [dbo].[EvidenceLineAllocations]
(
    [Id]         INT           IDENTITY(1,1) NOT NULL,
    [EvidenceId] INT           NOT NULL,
    [ItemId]     INT           NOT NULL,
    [Amount]     DECIMAL(18,2) NOT NULL,
    [RowVersion] ROWVERSION    NOT NULL,

    CONSTRAINT [PK_EvidenceLineAllocations] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [FK_EvidenceLineAllocations_Evidence]
        FOREIGN KEY ([EvidenceId]) REFERENCES [dbo].[Evidence]([Id]) ON DELETE CASCADE,
    CONSTRAINT [FK_EvidenceLineAllocations_Items]
        FOREIGN KEY ([ItemId]) REFERENCES [dbo].[Items]([Id]) ON DELETE NO ACTION,
    CONSTRAINT [CK_EvidenceLineAllocations_Amount_Positive] CHECK ([Amount] > 0),
    -- ≤1 allocation row per (evidence, line).
    CONSTRAINT [UX_EvidenceLineAlloc_Evidence_Item] UNIQUE ([EvidenceId], [ItemId])
);
GO

-- Covering index for the per-line evidence sums (completeness + closure).
CREATE NONCLUSTERED INDEX [IX_EvidenceLineAlloc_ItemId]
    ON [dbo].[EvidenceLineAllocations] ([ItemId]);
GO
