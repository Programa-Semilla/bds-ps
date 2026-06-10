CREATE TABLE [dbo].[Funds]
(
    [Id]                        INT            IDENTITY(1,1) NOT NULL,
    [Name]                      NVARCHAR(120)  NOT NULL,
    [Description]               NVARCHAR(2000) NOT NULL,
    [Status]                    TINYINT        NOT NULL CONSTRAINT [DF_Funds_Status] DEFAULT (0),
    -- Spec 029 / D3 — single optional regulation PDF stored via spec-014
    -- IObjectStorage; the blob key + metadata live as columns on the aggregate
    -- (mirrors FundingAgreement). All-or-nothing: set together / cleared together.
    [RegulationBlobKey]         NVARCHAR(1024) NULL,
    [RegulationFileName]        NVARCHAR(260)  NULL,
    [RegulationContentType]     NVARCHAR(100)  NULL,
    [RegulationSizeBytes]       BIGINT         NULL,
    [RegulationUploadedAtUtc]   DATETIME2(3)   NULL,
    [RegulationUploadedByUserId] NVARCHAR(450) NULL,
    [CreatedAt]                 DATETIMEOFFSET(0) NOT NULL CONSTRAINT [DF_Funds_CreatedAt] DEFAULT (SYSUTCDATETIME()),
    [RowVersion]                ROWVERSION     NOT NULL,

    CONSTRAINT [PK_Funds] PRIMARY KEY CLUSTERED ([Id])
);
GO

-- Spec 029 / D1 — Fund.Name uniqueness across the catalog. SQL Server default
-- collations are case-insensitive; the unique index is the authoritative gate
-- against duplicate names submitted concurrently. The service additionally
-- trims + pre-checks for a friendly es-CR message.
CREATE UNIQUE NONCLUSTERED INDEX [UX_Funds_Name]
    ON [dbo].[Funds] ([Name]);
GO
