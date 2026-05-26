CREATE TABLE [dbo].[Suppliers]
(
    [Id]                       INT             IDENTITY(1,1) NOT NULL,
    [LegalId]                  NVARCHAR(50)    NOT NULL,
    [IdentificationType]       TINYINT         NULL,            -- spec 026; NULL = unassigned (legacy row)
    [Name]                     NVARCHAR(300)   NOT NULL,
    [HasElectronicInvoice]     BIT             NOT NULL CONSTRAINT [DF_Suppliers_HasElectronicInvoice] DEFAULT (0),
    [IsCompliantCCSS]          BIT             NOT NULL CONSTRAINT [DF_Suppliers_IsCompliantCCSS] DEFAULT (0),
    [IsCompliantHacienda]      BIT             NOT NULL CONSTRAINT [DF_Suppliers_IsCompliantHacienda] DEFAULT (0),
    [IsCompliantSICOP]         BIT             NOT NULL CONSTRAINT [DF_Suppliers_IsCompliantSICOP] DEFAULT (0),

    -- Spec 013: lifecycle (FR-021, FR-024, FR-035). Default 2 = Verified so existing
    -- migrated rows land in Verified status without an explicit UPDATE.
    [VerificationStatus]       TINYINT         NOT NULL CONSTRAINT [DF_Suppliers_VerificationStatus] DEFAULT (2),
    [CreatedByApplicantId]     INT             NULL,
    [VerifiedByUserId]         NVARCHAR(450)   NULL,
    [VerifiedAt]               DATETIME2       NULL,
    [RejectionReason]          NVARCHAR(1000)  NULL,

    [CreatedAt]                DATETIME2       NOT NULL CONSTRAINT [DF_Suppliers_CreatedAt] DEFAULT (GETUTCDATE()),
    [UpdatedAt]                DATETIME2       NOT NULL CONSTRAINT [DF_Suppliers_UpdatedAt] DEFAULT (GETUTCDATE()),

    -- TODO[013-cleanup]: drop these six columns one release after migration ships
    -- (research.md R3). They are kept nullable for the migration's source-data read
    -- and are no longer referenced by EF Core (Ignore() in SupplierConfiguration).
    [ContactName]              NVARCHAR(200)   NULL,
    [Email]                    NVARCHAR(256)   NULL,
    [Phone]                    NVARCHAR(20)    NULL,
    [Location]                 NVARCHAR(500)   NULL,
    [ShippingDetails]          NVARCHAR(500)   NULL,
    [WarrantyInfo]             NVARCHAR(500)   NULL,

    CONSTRAINT [PK_Suppliers] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [UX_Suppliers_LegalId] UNIQUE ([LegalId]),
    CONSTRAINT [FK_Suppliers_Applicants] FOREIGN KEY ([CreatedByApplicantId]) REFERENCES [dbo].[Applicants] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_Suppliers_AspNetUsers] FOREIGN KEY ([VerifiedByUserId]) REFERENCES [dbo].[AspNetUsers] ([Id]) ON DELETE NO ACTION
);
GO

CREATE INDEX [IX_Suppliers_VerificationStatus] ON [dbo].[Suppliers] ([VerificationStatus]);
GO

CREATE INDEX [IX_Suppliers_Name] ON [dbo].[Suppliers] ([Name]);
GO
