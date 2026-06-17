-- Spec 036 — funds-usage evidence files uploaded by in-scope reviewers/admins
-- on an application that has reached AgreementExecuted. One row per uploaded
-- file. Greenfield additive table (no existing rows → no DEFAULT/backfill dance).
-- See specs/036-funds-usage-evidence/data-model.md.
CREATE TABLE [dbo].[FundsUsageEvidence]
(
    [Id]                INT            IDENTITY(1,1) NOT NULL,
    [ApplicationId]     INT            NOT NULL,
    [UploadedByUserId]  NVARCHAR(450)  NOT NULL,
    [OriginalFileName]  NVARCHAR(500)  NOT NULL,
    [BlobKey]           NVARCHAR(1024) NOT NULL,
    [FileSize]          BIGINT         NOT NULL,
    [ContentType]       NVARCHAR(100)  NOT NULL,
    [Note]              NVARCHAR(250)  NULL,
    [UploadedAt]        DATETIME2(3)   NOT NULL CONSTRAINT [DF_FundsUsageEvidence_UploadedAt] DEFAULT (GETUTCDATE()),
    [RowVersion]        ROWVERSION     NOT NULL,

    CONSTRAINT [PK_FundsUsageEvidence] PRIMARY KEY CLUSTERED ([Id]),
    -- Applications are soft-deleted, never hard-deleted, so NO ACTION is safe and
    -- avoids any multiple-cascade-path publish failure (the spec-029/035 lesson).
    CONSTRAINT [FK_FundsUsageEvidence_Applications]
        FOREIGN KEY ([ApplicationId]) REFERENCES [dbo].[Applications]([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_FundsUsageEvidence_AspNetUsers]
        FOREIGN KEY ([UploadedByUserId]) REFERENCES [dbo].[AspNetUsers]([Id]) ON DELETE NO ACTION,
    CONSTRAINT [CK_FundsUsageEvidence_FileSize_Positive] CHECK ([FileSize] > 0)
);
GO

CREATE NONCLUSTERED INDEX [IX_FundsUsageEvidence_ApplicationId]
    ON [dbo].[FundsUsageEvidence] ([ApplicationId]);
GO
