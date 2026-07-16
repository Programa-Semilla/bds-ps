-- Spec 045 — one typed evidence document (bank receipt or invoice) per disbursement.
-- Kind is a TINYINT enum (EF HasConversion<byte>()); Amount is the reconciled figure
-- (DECIMAL(18,2)); Currency is a CHAR(3) code (CRC in P1). The filtered/plain UNIQUE
-- on (DisbursementId, Kind) enforces exactly one bank receipt + one invoice (FR-006).
-- See specs/045-financial-disbursement-core/data-model.md.
CREATE TABLE [dbo].[DisbursementEvidence]
(
    [Id]                      INT               IDENTITY(1,1) NOT NULL,
    [DisbursementId]          INT               NOT NULL,
    [Kind]                    TINYINT           NOT NULL,
    [Amount]                  DECIMAL(18,2)     NOT NULL,
    [Currency]                CHAR(3)           NOT NULL,
    [DocumentReferenceNumber] NVARCHAR(100)     NOT NULL,
    [DocumentDate]            DATE              NOT NULL,
    [OriginalFileName]        NVARCHAR(500)     NOT NULL,
    [BlobKey]                 NVARCHAR(1024)    NOT NULL,
    [FileSize]                BIGINT            NOT NULL,
    [ContentType]             NVARCHAR(100)     NOT NULL,
    [UploadedByUserId]        NVARCHAR(450)     NOT NULL,
    [UploadedAtUtc]           DATETIMEOFFSET(0)  NOT NULL CONSTRAINT [DF_DisbursementEvidence_UploadedAtUtc] DEFAULT (SYSUTCDATETIME()),
    [RowVersion]              ROWVERSION        NOT NULL,

    CONSTRAINT [PK_DisbursementEvidence] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [FK_DisbursementEvidence_Disbursements]
        FOREIGN KEY ([DisbursementId]) REFERENCES [dbo].[Disbursements]([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_DisbursementEvidence_AspNetUsers]
        FOREIGN KEY ([UploadedByUserId]) REFERENCES [dbo].[AspNetUsers]([Id]) ON DELETE NO ACTION,
    CONSTRAINT [CK_DisbursementEvidence_Amount_Positive] CHECK ([Amount] > 0),
    CONSTRAINT [CK_DisbursementEvidence_FileSize_Positive] CHECK ([FileSize] > 0),
    -- FR-006 / 1:1 per kind — exactly one bank receipt + one invoice per disbursement.
    CONSTRAINT [UX_DisbursementEvidence_Disbursement_Kind] UNIQUE ([DisbursementId], [Kind])
);
GO

CREATE NONCLUSTERED INDEX [IX_DisbursementEvidence_DisbursementId]
    ON [dbo].[DisbursementEvidence] ([DisbursementId]);
GO
