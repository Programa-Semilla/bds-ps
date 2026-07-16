CREATE TABLE [dbo].[Items]
(
    [Id]                          INT            IDENTITY(1,1) NOT NULL,
    [ApplicationId]               INT            NOT NULL,
    [LineCode]                    NVARCHAR(16)   NULL,
    [ProductName]                 NVARCHAR(500)  NOT NULL,
    [CategoryId]                  INT            NOT NULL,
    -- Spec 035 (evolved 2026-06-16, data-model.md D14) — the line item no longer carries its
    -- own impact template/values. It is ATTRIBUTED to the application's declared impacts via
    -- dbo.ItemImpacts and carries a single short justification (required at submit, <=300).
    [ImpactJustification]         NVARCHAR(300)  NULL,
    [ReviewStatus]                INT            NOT NULL CONSTRAINT [DF_Items_ReviewStatus] DEFAULT (0),
    [ReviewComment]               NVARCHAR(2000) NULL,
    [SelectedSupplierId]          INT            NULL,
    [IsNotTechnicallyEquivalent]  BIT            NOT NULL CONSTRAINT [DF_Items_IsNotTechnicallyEquivalent] DEFAULT (0),
    -- Spec 046 — budget-line tranche membership + off-ledger commit status. Both are
    -- nullable-safe inline adds (no post-deploy backfill — spec 032/037 precedent):
    -- TrancheId NULL = the virtual default tranche; CommitState default 0 = Uncommitted.
    [TrancheId]                   INT            NULL,
    [CommitState]                 TINYINT        NOT NULL CONSTRAINT [DF_Items_CommitState] DEFAULT (0),
    [CreatedAt]                   DATETIME2      NOT NULL CONSTRAINT [DF_Items_CreatedAt] DEFAULT (GETUTCDATE()),
    [UpdatedAt]                   DATETIME2      NOT NULL,

    CONSTRAINT [PK_Items] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [FK_Items_Applications] FOREIGN KEY ([ApplicationId]) REFERENCES [dbo].[Applications] ([Id]) ON DELETE CASCADE,
    CONSTRAINT [FK_Items_Categories] FOREIGN KEY ([CategoryId]) REFERENCES [dbo].[Categories] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_Items_Suppliers_SelectedSupplierId] FOREIGN KEY ([SelectedSupplierId]) REFERENCES [dbo].[Suppliers] ([Id]),
    -- NO ACTION: deleting a tranche re-parents its member lines to TrancheId=NULL in the
    -- domain (Application.DeleteTranche) first, so the DB never needs to cascade here.
    CONSTRAINT [FK_Items_Tranches] FOREIGN KEY ([TrancheId]) REFERENCES [dbo].[Tranches] ([Id]) ON DELETE NO ACTION
);
GO

-- Spec 046 — filtered index for tranche membership lookups (only assigned lines).
CREATE NONCLUSTERED INDEX [IX_Items_TrancheId]
    ON [dbo].[Items] ([TrancheId])
    WHERE [TrancheId] IS NOT NULL;
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
