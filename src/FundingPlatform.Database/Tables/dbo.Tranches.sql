-- Spec 046 — a per-application named funding phase that groups the application's
-- line items (budget-lines). The tranche AMOUNT is NOT stored: it is derived at
-- projection time as Σ member-line budgets, so "Σ tranche = allocation" holds by
-- construction (research D4). Unassigned lines fall into a virtual default tranche
-- (no row), so an application with no tranches needs zero migration. Additive table.
-- See specs/046-tranches-budget-lines/data-model.md (Aggregate 1).
CREATE TABLE [dbo].[Tranches]
(
    [Id]            INT               IDENTITY(1,1) NOT NULL,
    [ApplicationId] INT               NOT NULL,
    [Name]          NVARCHAR(60)      NOT NULL,
    [Ordinal]       INT               NOT NULL,
    [CreatedAtUtc]  DATETIMEOFFSET(0) NOT NULL CONSTRAINT [DF_Tranches_CreatedAtUtc] DEFAULT (SYSUTCDATETIME()),
    [UpdatedAtUtc]  DATETIMEOFFSET(0) NOT NULL CONSTRAINT [DF_Tranches_UpdatedAtUtc] DEFAULT (SYSUTCDATETIME()),
    [RowVersion]    ROWVERSION        NOT NULL,

    CONSTRAINT [PK_Tranches] PRIMARY KEY CLUSTERED ([Id]),
    -- Applications are soft-deleted, never hard-deleted, so NO ACTION is safe and avoids a
    -- multiple-cascade-path publish failure (Items reach here via TrancheId — see dbo.Items.sql).
    CONSTRAINT [FK_Tranches_Applications]
        FOREIGN KEY ([ApplicationId]) REFERENCES [dbo].[Applications]([Id]) ON DELETE NO ACTION
);
GO

CREATE NONCLUSTERED INDEX [IX_Tranches_ApplicationId]
    ON [dbo].[Tranches] ([ApplicationId]);
GO

-- One tranche name per application (DB backstop; the accent/case pre-check lives in the
-- service, mirroring CompanyNameNormalizer). Case-insensitivity comes from the column collation.
CREATE UNIQUE INDEX [UX_Tranches_ApplicationId_Name]
    ON [dbo].[Tranches] ([ApplicationId], [Name]);
GO
