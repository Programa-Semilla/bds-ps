-- Spec 046 — the M:N join realizing per-line payment attribution: a portion of a
-- Disbursement attributed to one committed budget-line (Item). Owned by the
-- Disbursement (replace-all set on Record/Edit). Follows the dbo.ItemImpacts topology
-- exactly: two FK paths reach [Applications] (via Disbursements and via Items), so only
-- the Disbursements path is CASCADE and the Items path is NO ACTION — otherwise the
-- dacpac publish fails with a multiple-cascade-path error (the spec-029/035 lesson).
-- See specs/046-tranches-budget-lines/data-model.md (Aggregate 2).
CREATE TABLE [dbo].[DisbursementLineAllocations]
(
    [Id]             INT           IDENTITY(1,1) NOT NULL,
    [DisbursementId] INT           NOT NULL,
    [ItemId]         INT           NOT NULL,
    [Amount]         DECIMAL(18,2) NOT NULL,
    [RowVersion]     ROWVERSION    NOT NULL,

    CONSTRAINT [PK_DisbursementLineAllocations] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [FK_DisbursementLineAllocations_Disbursements]
        FOREIGN KEY ([DisbursementId]) REFERENCES [dbo].[Disbursements]([Id]) ON DELETE CASCADE,
    CONSTRAINT [FK_DisbursementLineAllocations_Items]
        FOREIGN KEY ([ItemId]) REFERENCES [dbo].[Items]([Id]) ON DELETE NO ACTION,
    CONSTRAINT [CK_DisbursementLineAllocations_Amount_Positive] CHECK ([Amount] > 0),
    -- ≤1 attribution row per (disbursement, line).
    CONSTRAINT [UX_DisbLineAlloc_Disbursement_Item] UNIQUE ([DisbursementId], [ItemId])
);
GO

-- Covering index for the per-line payment sums (Paid/Validated/Pending composition).
CREATE NONCLUSTERED INDEX [IX_DisbLineAlloc_ItemId]
    ON [dbo].[DisbursementLineAllocations] ([ItemId]);
GO
