CREATE TABLE [dbo].[FundingAgreements]
(
    [Id]                INT            IDENTITY(1,1) NOT NULL,
    [ApplicationId]     INT            NOT NULL,
    [FileName]          NVARCHAR(260)  NOT NULL,
    [ContentType]       NVARCHAR(100)  NOT NULL,
    [Size]              BIGINT         NOT NULL,
    [StoragePath]       NVARCHAR(500)  NOT NULL,
    [BlobKey]           NVARCHAR(1024) NOT NULL,
    [GeneratedAtUtc]    DATETIME2(3)   NOT NULL,
    [GeneratedByUserId] NVARCHAR(450)  NOT NULL,
    [GeneratedVersion]  INT            NOT NULL CONSTRAINT DF_FundingAgreements_GeneratedVersion DEFAULT(1),
    [RowVersion]        ROWVERSION     NOT NULL,

    CONSTRAINT [PK_FundingAgreements] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [UQ_FundingAgreements_ApplicationId] UNIQUE ([ApplicationId]),
    CONSTRAINT [FK_FundingAgreements_Applications]
        FOREIGN KEY ([ApplicationId]) REFERENCES [dbo].[Applications] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_FundingAgreements_AspNetUsers]
        FOREIGN KEY ([GeneratedByUserId]) REFERENCES [dbo].[AspNetUsers] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [CK_FundingAgreements_Size_Positive] CHECK ([Size] > 0),
    CONSTRAINT [CK_FundingAgreements_GeneratedVersion_Positive] CHECK ([GeneratedVersion] >= 1)
);
GO

CREATE NONCLUSTERED INDEX [IX_FundingAgreements_GeneratedByUserId]
    ON [dbo].[FundingAgreements] ([GeneratedByUserId]);
GO
