-- Spec 045 — the mutable operational disbursement record against an executed
-- funding agreement. Standalone aggregate keyed by ApplicationId (research R2),
-- like dbo.FundsUsageEvidence. Greenfield additive table (no DEFAULT/backfill
-- dance). State is a TINYINT enum (EF HasConversion<byte>()); money is exact
-- DECIMAL(18,2). See specs/045-financial-disbursement-core/data-model.md.
CREATE TABLE [dbo].[Disbursements]
(
    [Id]                       INT               IDENTITY(1,1) NOT NULL,
    [ApplicationId]            INT               NOT NULL,
    [PaymentDate]              DATE              NOT NULL,
    [Amount]                   DECIMAL(18,2)     NOT NULL,
    [BankTransactionReference] NVARCHAR(100)     NOT NULL,
    [BankAccountReference]     NVARCHAR(100)     NULL,
    [State]                    TINYINT           NOT NULL CONSTRAINT [DF_Disbursements_State] DEFAULT (0),
    [CreatedByUserId]          NVARCHAR(450)     NOT NULL,
    [CreatedAtUtc]             DATETIMEOFFSET(0)  NOT NULL CONSTRAINT [DF_Disbursements_CreatedAtUtc] DEFAULT (SYSUTCDATETIME()),
    [ValidatedByUserId]        NVARCHAR(450)     NULL,
    [ValidatedAtUtc]           DATETIMEOFFSET(0)  NULL,
    [CancelledByUserId]        NVARCHAR(450)     NULL,
    [CancelledAtUtc]           DATETIMEOFFSET(0)  NULL,
    [RowVersion]               ROWVERSION        NOT NULL,

    CONSTRAINT [PK_Disbursements] PRIMARY KEY CLUSTERED ([Id]),
    -- Applications are soft-deleted, never hard-deleted, so NO ACTION is safe and
    -- avoids any multiple-cascade-path publish failure (the spec-029/035 lesson).
    CONSTRAINT [FK_Disbursements_Applications]
        FOREIGN KEY ([ApplicationId]) REFERENCES [dbo].[Applications]([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_Disbursements_AspNetUsers_CreatedBy]
        FOREIGN KEY ([CreatedByUserId]) REFERENCES [dbo].[AspNetUsers]([Id]) ON DELETE NO ACTION,
    CONSTRAINT [CK_Disbursements_Amount_Positive] CHECK ([Amount] > 0)
);
GO

CREATE NONCLUSTERED INDEX [IX_Disbursements_ApplicationId]
    ON [dbo].[Disbursements] ([ApplicationId]);
GO

CREATE NONCLUSTERED INDEX [IX_Disbursements_ApplicationId_State]
    ON [dbo].[Disbursements] ([ApplicationId], [State]);
GO
