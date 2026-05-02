CREATE TABLE [dbo].[Documents]
(
    [Id]               INT            IDENTITY(1,1) NOT NULL,
    [OriginalFileName] NVARCHAR(500)  NOT NULL,
    [BlobKey]          NVARCHAR(1024) NOT NULL,
    [FileSize]         BIGINT         NOT NULL,
    [ContentType]      NVARCHAR(100)  NOT NULL,
    [UploadedAt]       DATETIME2      NOT NULL CONSTRAINT [DF_Documents_UploadedAt] DEFAULT (GETUTCDATE()),

    CONSTRAINT [PK_Documents] PRIMARY KEY CLUSTERED ([Id])
);
