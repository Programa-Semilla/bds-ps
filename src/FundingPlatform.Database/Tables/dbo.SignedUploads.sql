CREATE TABLE [dbo].[SignedUploads]
(
    [Id]                        INT            IDENTITY(1,1) NOT NULL,
    [FundingAgreementId]        INT            NOT NULL,
    [UploaderUserId]            NVARCHAR(450)  NOT NULL,
    [GeneratedVersionAtUpload]  INT            NOT NULL,
    [FileName]                  NVARCHAR(260)  NOT NULL,
    [ContentType]               NVARCHAR(100)  NOT NULL CONSTRAINT DF_SignedUploads_ContentType DEFAULT('application/pdf'),
    [Size]                      BIGINT         NOT NULL,
    [StoragePath]               NVARCHAR(1024) NOT NULL,
    [BlobKey]                   NVARCHAR(1024) NOT NULL,
    [UploadedAtUtc]             DATETIME2(3)   NOT NULL,
    [Status]                    INT            NOT NULL,
    [RowVersion]                ROWVERSION     NOT NULL,

    CONSTRAINT [PK_SignedUploads] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [FK_SignedUploads_FundingAgreements]
        FOREIGN KEY ([FundingAgreementId]) REFERENCES [dbo].[FundingAgreements]([Id]) ON DELETE CASCADE,
    CONSTRAINT [FK_SignedUploads_AspNetUsers]
        FOREIGN KEY ([UploaderUserId]) REFERENCES [dbo].[AspNetUsers]([Id]) ON DELETE NO ACTION,
    CONSTRAINT [CK_SignedUploads_Size_Positive] CHECK ([Size] > 0),
    CONSTRAINT [CK_SignedUploads_Status_Range] CHECK ([Status] BETWEEN 0 AND 4)
);
GO

CREATE NONCLUSTERED INDEX [IX_SignedUploads_FundingAgreementId_Status]
    ON [dbo].[SignedUploads] ([FundingAgreementId], [Status]);
GO

CREATE NONCLUSTERED INDEX [IX_SignedUploads_UploaderUserId]
    ON [dbo].[SignedUploads] ([UploaderUserId]);
GO

CREATE UNIQUE NONCLUSTERED INDEX [UX_SignedUploads_OnePending_PerAgreement]
    ON [dbo].[SignedUploads] ([FundingAgreementId])
    WHERE [Status] = 0;
GO
