-- Spec 045 — the append-only balance ledger (FR-017/FR-018). Exactly one Allocation
-- entry per application (filtered-unique on EntryType=0) and exactly one Disbursement
-- entry per validated disbursement (filtered-unique on DisbursementId WHERE EntryType=1,
-- giving idempotency / no double-post). EntryType is a TINYINT enum
-- (EF HasConversion<byte>()); Amount is exact DECIMAL(18,2). Never updated or deleted.
-- See specs/045-financial-disbursement-core/data-model.md.
CREATE TABLE [dbo].[DisbursementLedgerEntries]
(
    [Id]              INT               IDENTITY(1,1) NOT NULL,
    [ApplicationId]   INT               NOT NULL,
    [EntryType]       TINYINT           NOT NULL,
    [Amount]          DECIMAL(18,2)     NOT NULL,
    [DisbursementId]  INT               NULL,
    [PostedByUserId]  NVARCHAR(450)     NOT NULL,
    [PostedAtUtc]     DATETIMEOFFSET(0)  NOT NULL CONSTRAINT [DF_DisbursementLedgerEntries_PostedAtUtc] DEFAULT (SYSUTCDATETIME()),
    [RowVersion]      ROWVERSION        NOT NULL,

    CONSTRAINT [PK_DisbursementLedgerEntries] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [FK_DisbursementLedgerEntries_Applications]
        FOREIGN KEY ([ApplicationId]) REFERENCES [dbo].[Applications]([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_DisbursementLedgerEntries_Disbursements]
        FOREIGN KEY ([DisbursementId]) REFERENCES [dbo].[Disbursements]([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_DisbursementLedgerEntries_AspNetUsers]
        FOREIGN KEY ([PostedByUserId]) REFERENCES [dbo].[AspNetUsers]([Id]) ON DELETE NO ACTION
);
GO

CREATE NONCLUSTERED INDEX [IX_DisbursementLedgerEntries_ApplicationId_EntryType]
    ON [dbo].[DisbursementLedgerEntries] ([ApplicationId], [EntryType]);
GO

-- Exactly one Allocation entry per application (the approved-ceiling snapshot).
CREATE UNIQUE NONCLUSTERED INDEX [UX_DisbursementLedger_Allocation]
    ON [dbo].[DisbursementLedgerEntries] ([ApplicationId])
    WHERE [EntryType] = 0;
GO

-- One immutable Disbursement entry per validated disbursement (no double-post / idempotency).
CREATE UNIQUE NONCLUSTERED INDEX [UX_DisbursementLedger_Disbursement]
    ON [dbo].[DisbursementLedgerEntries] ([DisbursementId])
    WHERE [EntryType] = 1;
GO
