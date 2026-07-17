-- Spec 047 — the evidence-graph node: a typed supporting document (bank receipt, invoice,
-- signed acceptance, credit note, refund receipt, other) attached to an executed application
-- and linked M:N to budget-lines. Lives ALONGSIDE the untouched dbo.DisbursementEvidence
-- money-gate (research D1). Type is a TINYINT enum (EF HasConversion<byte>()); Amount is exact
-- DECIMAL(18,2); Currency a CHAR(3) code (CRC in P3); FileHash a CHAR(64) SHA-256. The row carries
-- the CURRENT denormalized file+critical values; dbo.EvidenceVersions is the audit chain.
-- FK to Applications is NO ACTION (soft-delete filter model, matches dbo.Disbursements).
-- See specs/047-evidence-graph-required-docs/data-model.md.
CREATE TABLE [dbo].[Evidence]
(
    [Id]                      INT               IDENTITY(1,1) NOT NULL,
    [ApplicationId]           INT               NOT NULL,
    [DisbursementId]          INT               NULL,
    [Type]                    TINYINT           NOT NULL,
    [Amount]                  DECIMAL(18,2)     NOT NULL,
    [Currency]                CHAR(3)           NOT NULL,
    [DocumentReferenceNumber] NVARCHAR(100)     NOT NULL,
    [DocumentDate]            DATE              NOT NULL,
    [SupplierId]              INT               NULL,
    [BlobKey]                 NVARCHAR(1024)    NOT NULL,
    [OriginalFileName]        NVARCHAR(500)     NOT NULL,
    [FileSize]                BIGINT            NOT NULL,
    [ContentType]             NVARCHAR(100)     NOT NULL,
    [FileHash]                CHAR(64)          NOT NULL,
    [UploadedByUserId]        NVARCHAR(450)     NOT NULL,
    [UploadedAtUtc]           DATETIMEOFFSET(0)  NOT NULL CONSTRAINT [DF_Evidence_UploadedAtUtc] DEFAULT (SYSUTCDATETIME()),
    [RowVersion]              ROWVERSION        NOT NULL,

    CONSTRAINT [PK_Evidence] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [FK_Evidence_Applications]
        FOREIGN KEY ([ApplicationId]) REFERENCES [dbo].[Applications]([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_Evidence_Disbursements]
        FOREIGN KEY ([DisbursementId]) REFERENCES [dbo].[Disbursements]([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_Evidence_Suppliers]
        FOREIGN KEY ([SupplierId]) REFERENCES [dbo].[Suppliers]([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_Evidence_AspNetUsers]
        FOREIGN KEY ([UploadedByUserId]) REFERENCES [dbo].[AspNetUsers]([Id]) ON DELETE NO ACTION,
    CONSTRAINT [CK_Evidence_Amount_Positive] CHECK ([Amount] > 0),
    CONSTRAINT [CK_Evidence_FileSize_Positive] CHECK ([FileSize] > 0)
);
GO

CREATE NONCLUSTERED INDEX [IX_Evidence_ApplicationId]
    ON [dbo].[Evidence] ([ApplicationId]);
GO
