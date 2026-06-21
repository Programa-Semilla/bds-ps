CREATE TABLE [dbo].[Suppliers]
(
    [Id]                       INT             IDENTITY(1,1) NOT NULL,
    [LegalId]                  NVARCHAR(50)    NOT NULL,
    [IdentificationType]       TINYINT         NULL,            -- spec 026; NULL = unassigned (legacy row)
    [Name]                     NVARCHAR(300)   NOT NULL,

    -- Spec 038: provider regulatory compliance (auditor-maintained). Replaces the
    -- four BIT compliance/e-invoice columns. NULL status = "sin revisar". Each
    -- status carries per-field last-reviewed metadata for the freshness display.
    [HaciendaStatus]           TINYINT         NULL,
    [HaciendaLastReviewedAt]   DATETIME2       NULL,
    [HaciendaLastReviewedBy]   NVARCHAR(450)   NULL,
    [HaciendaLastReviewedSource] TINYINT       NULL,
    [CcssStatus]               TINYINT         NULL,
    [CcssLastReviewedAt]       DATETIME2       NULL,
    [CcssLastReviewedBy]       NVARCHAR(450)   NULL,
    [CcssLastReviewedSource]   TINYINT         NULL,
    [SicopStatus]              TINYINT         NULL,
    [SicopLastReviewedAt]      DATETIME2       NULL,
    [SicopLastReviewedBy]      NVARCHAR(450)   NULL,
    [SicopLastReviewedSource]  TINYINT         NULL,
    [IsPmeOrPyme]              BIT             NOT NULL CONSTRAINT [DF_Suppliers_IsPmeOrPyme] DEFAULT (0),
    [HasWarning]               BIT             NOT NULL CONSTRAINT [DF_Suppliers_HasWarning] DEFAULT (0),
    [WarningNote]              NVARCHAR(1000)  NULL,

    -- Spec 043: per-provider Hacienda daily-sync outcome (read by the supplier
    -- detail + admin-list "verificación fallida" filter). All nullable → migration-safe,
    -- no backfill. NULL outcome = never synced.
    [HaciendaSyncAttemptAt]    DATETIME2       NULL,
    [HaciendaSyncOutcome]      TINYINT         NULL,
    [HaciendaSyncError]        NVARCHAR(500)   NULL,

    [RowVersion]               ROWVERSION      NOT NULL,

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
    CONSTRAINT [FK_Suppliers_AspNetUsers] FOREIGN KEY ([VerifiedByUserId]) REFERENCES [dbo].[AspNetUsers] ([Id]) ON DELETE NO ACTION,
    -- Spec 038: per-field last-reviewer FKs (NO ACTION, consistent with VerifiedByUserId).
    CONSTRAINT [FK_Suppliers_HaciendaReviewedBy_AspNetUsers] FOREIGN KEY ([HaciendaLastReviewedBy]) REFERENCES [dbo].[AspNetUsers] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_Suppliers_CcssReviewedBy_AspNetUsers] FOREIGN KEY ([CcssLastReviewedBy]) REFERENCES [dbo].[AspNetUsers] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_Suppliers_SicopReviewedBy_AspNetUsers] FOREIGN KEY ([SicopLastReviewedBy]) REFERENCES [dbo].[AspNetUsers] ([Id]) ON DELETE NO ACTION
);
GO

CREATE INDEX [IX_Suppliers_VerificationStatus] ON [dbo].[Suppliers] ([VerificationStatus]);
GO

CREATE INDEX [IX_Suppliers_Name] ON [dbo].[Suppliers] ([Name]);
GO
