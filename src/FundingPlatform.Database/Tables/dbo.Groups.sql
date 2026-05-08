CREATE TABLE [dbo].[Groups]
(
    [Id]        INT             IDENTITY(1,1) NOT NULL,
    [Name]      NVARCHAR(100)   COLLATE Latin1_General_CI_AI NOT NULL,
    [CreatedAt] DATETIMEOFFSET  NOT NULL CONSTRAINT [DF_Groups_CreatedAt] DEFAULT (SYSUTCDATETIME()),
    [UpdatedAt] DATETIMEOFFSET  NOT NULL CONSTRAINT [DF_Groups_UpdatedAt] DEFAULT (SYSUTCDATETIME()),

    CONSTRAINT [PK_Groups] PRIMARY KEY CLUSTERED ([Id])
);
GO

-- Spec 016 / FR-001 — case- and accent-insensitive uniqueness on Name (column collation
-- already provides case/accent insensitivity; the unique index is the authoritative gate
-- against duplicates submitted by two admins concurrently).
CREATE UNIQUE NONCLUSTERED INDEX [UX_Groups_Name]
    ON [dbo].[Groups] ([Name]);
GO
