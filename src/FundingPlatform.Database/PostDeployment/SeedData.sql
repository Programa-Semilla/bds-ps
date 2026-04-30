/*
    Post-Deployment Script: SeedData.sql
    Idempotent seed data for the FundingPlatform database.
*/

-- =============================================================================
-- Identity Roles
-- =============================================================================
IF NOT EXISTS (SELECT 1 FROM [dbo].[AspNetRoles] WHERE [NormalizedName] = N'APPLICANT')
    INSERT INTO [dbo].[AspNetRoles] ([Id], [Name], [NormalizedName], [ConcurrencyStamp])
    VALUES (NEWID(), N'Applicant', N'APPLICANT', NEWID());

IF NOT EXISTS (SELECT 1 FROM [dbo].[AspNetRoles] WHERE [NormalizedName] = N'ADMIN')
    INSERT INTO [dbo].[AspNetRoles] ([Id], [Name], [NormalizedName], [ConcurrencyStamp])
    VALUES (NEWID(), N'Admin', N'ADMIN', NEWID());

IF NOT EXISTS (SELECT 1 FROM [dbo].[AspNetRoles] WHERE [NormalizedName] = N'REVIEWER')
    INSERT INTO [dbo].[AspNetRoles] ([Id], [Name], [NormalizedName], [ConcurrencyStamp])
    VALUES (NEWID(), N'Reviewer', N'REVIEWER', NEWID());

-- =============================================================================
-- Categories
-- =============================================================================
IF NOT EXISTS (SELECT 1 FROM [dbo].[Categories] WHERE [Name] = N'Computing Equipment')
    INSERT INTO [dbo].[Categories] ([Name], [Description], [IsActive])
    VALUES (N'Computing Equipment', N'Computers, servers, networking equipment, and peripherals', 1);

IF NOT EXISTS (SELECT 1 FROM [dbo].[Categories] WHERE [Name] = N'Laboratory Equipment')
    INSERT INTO [dbo].[Categories] ([Name], [Description], [IsActive])
    VALUES (N'Laboratory Equipment', N'Scientific instruments, lab apparatus, and research equipment', 1);

IF NOT EXISTS (SELECT 1 FROM [dbo].[Categories] WHERE [Name] = N'Software')
    INSERT INTO [dbo].[Categories] ([Name], [Description], [IsActive])
    VALUES (N'Software', N'Software licenses, subscriptions, and development tools', 1);

IF NOT EXISTS (SELECT 1 FROM [dbo].[Categories] WHERE [Name] = N'Office Equipment')
    INSERT INTO [dbo].[Categories] ([Name], [Description], [IsActive])
    VALUES (N'Office Equipment', N'Furniture, office supplies, and general office equipment', 1);

IF NOT EXISTS (SELECT 1 FROM [dbo].[Categories] WHERE [Name] = N'Vehicles')
    INSERT INTO [dbo].[Categories] ([Name], [Description], [IsActive])
    VALUES (N'Vehicles', N'Transport vehicles and related equipment', 1);

IF NOT EXISTS (SELECT 1 FROM [dbo].[Categories] WHERE [Name] = N'Construction')
    INSERT INTO [dbo].[Categories] ([Name], [Description], [IsActive])
    VALUES (N'Construction', N'Building materials, construction equipment, and infrastructure', 1);

-- =============================================================================
-- System Configurations
-- =============================================================================
IF NOT EXISTS (SELECT 1 FROM [dbo].[SystemConfigurations] WHERE [Key] = N'MinQuotationsPerItem')
    INSERT INTO [dbo].[SystemConfigurations] ([Key], [Value], [Description], [UpdatedAt])
    VALUES (N'MinQuotationsPerItem', N'2', N'Minimum number of quotations required per item', GETUTCDATE());

IF NOT EXISTS (SELECT 1 FROM [dbo].[SystemConfigurations] WHERE [Key] = N'AllowedFileTypes')
    INSERT INTO [dbo].[SystemConfigurations] ([Key], [Value], [Description], [UpdatedAt])
    VALUES (N'AllowedFileTypes', N'.pdf,.jpg,.jpeg,.png,.doc,.docx,.xls,.xlsx', N'Comma-separated list of allowed file extensions for uploads', GETUTCDATE());

IF NOT EXISTS (SELECT 1 FROM [dbo].[SystemConfigurations] WHERE [Key] = N'MaxFileSizeMB')
    INSERT INTO [dbo].[SystemConfigurations] ([Key], [Value], [Description], [UpdatedAt])
    VALUES (N'MaxFileSizeMB', N'10', N'Maximum file size in megabytes for uploads', GETUTCDATE());

IF NOT EXISTS (SELECT 1 FROM [dbo].[SystemConfigurations] WHERE [Key] = N'MaxAppealsPerApplication')
    INSERT INTO [dbo].[SystemConfigurations] ([Key], [Value], [Description], [UpdatedAt])
    VALUES (N'MaxAppealsPerApplication', N'1', N'Maximum appeals per application across all reopen cycles. 0 disables appeals.', GETUTCDATE());

IF NOT EXISTS (SELECT 1 FROM [dbo].[SystemConfigurations] WHERE [Key] = N'DefaultCurrency')
    INSERT INTO [dbo].[SystemConfigurations] ([Key], [Value], [Description], [UpdatedAt])
    VALUES (N'DefaultCurrency', N'$(DefaultCurrency)', N'Default 3-character ISO 4217 currency code applied to new quotations and historical backfill', GETUTCDATE());

-- Backfill any quotations missing a Currency with the configured DefaultCurrency.
-- Idempotent: re-runs are no-ops because every prior row already has Currency set.
UPDATE [dbo].[Quotations]
    SET [Currency] = (SELECT [Value] FROM [dbo].[SystemConfigurations] WHERE [Key] = N'DefaultCurrency')
    WHERE [Currency] IS NULL;

-- Tighten Quotations.Currency to NOT NULL after backfill. Idempotent: a second
-- invocation finds the column already NOT NULL and short-circuits. Inlined here
-- because Microsoft.Build.Sql 2.1.0 supports a single post-deploy script per
-- project; deployment ordering (column add (declarative) -> backfill -> tighten)
-- is preserved by sequential placement within this file.
IF EXISTS (
    SELECT 1
    FROM sys.columns c
    INNER JOIN sys.tables t ON t.object_id = c.object_id
    WHERE t.name = N'Quotations' AND c.name = N'Currency' AND c.is_nullable = 1
)
BEGIN
    ALTER TABLE [dbo].[Quotations] ALTER COLUMN [Currency] NVARCHAR(3) NOT NULL;
END;

-- =============================================================================
-- Impact Templates
-- =============================================================================

-- Template: Increase Production Capacity
IF NOT EXISTS (SELECT 1 FROM [dbo].[ImpactTemplates] WHERE [Name] = N'Increase Production Capacity')
BEGIN
    INSERT INTO [dbo].[ImpactTemplates] ([Name], [Description], [IsActive], [UpdatedAt])
    VALUES (N'Increase Production Capacity', N'Measures the expected increase in production capacity resulting from the funded item', 1, GETUTCDATE());

    DECLARE @IncreaseCapacityId INT = SCOPE_IDENTITY();

    INSERT INTO [dbo].[ImpactTemplateParameters] ([ImpactTemplateId], [Name], [DisplayLabel], [DataType], [IsRequired], [SortOrder])
    VALUES
        (@IncreaseCapacityId, N'CurrentCapacity',    N'Current Capacity',     1, 1, 1),
        (@IncreaseCapacityId, N'ProjectedCapacity',   N'Projected Capacity',   1, 1, 2),
        (@IncreaseCapacityId, N'TimeframeInMonths',   N'Timeframe in Months',  2, 1, 3);
END;

-- Template: Job Creation
IF NOT EXISTS (SELECT 1 FROM [dbo].[ImpactTemplates] WHERE [Name] = N'Job Creation')
BEGIN
    INSERT INTO [dbo].[ImpactTemplates] ([Name], [Description], [IsActive], [UpdatedAt])
    VALUES (N'Job Creation', N'Measures the expected number of new jobs created as a result of the funded item', 1, GETUTCDATE());

    DECLARE @JobCreationId INT = SCOPE_IDENTITY();

    INSERT INTO [dbo].[ImpactTemplateParameters] ([ImpactTemplateId], [Name], [DisplayLabel], [DataType], [IsRequired], [SortOrder])
    VALUES
        (@JobCreationId, N'CurrentEmployees',   N'Current Employees',    2, 1, 1),
        (@JobCreationId, N'ProjectedNewJobs',    N'Projected New Jobs',   2, 1, 2),
        (@JobCreationId, N'JobType',             N'Job Type',             0, 1, 3);
END;
GO

-- =============================================================================
-- Spec 013: Supplier Catalog migration. Idempotent forward-only.
-- Microsoft.Build.Sql 2.1.0 only supports a single PostDeploy script, so the
-- migration is inlined here matching the spec 010 currency-migration pattern
-- already present above.
-- =============================================================================
IF EXISTS (SELECT 1 FROM sys.columns
           WHERE object_id = OBJECT_ID(N'dbo.Suppliers') AND name = N'ContactName')
   AND NOT EXISTS (SELECT 1 FROM [dbo].[SupplierBranches])
   AND EXISTS (SELECT 1 FROM [dbo].[Suppliers])
BEGIN
    BEGIN TRY
        BEGIN TRANSACTION;

        DECLARE @SystemAdminId NVARCHAR(450) = (
            SELECT TOP 1 [Id] FROM [dbo].[AspNetUsers]
            WHERE [IsSystemSentinel] = 1
        );

        IF @SystemAdminId IS NULL
            THROW 50010, 'Migration 013: system admin sentinel not found. Spec 009 must be deployed first.', 1;

        UPDATE [dbo].[Suppliers]
        SET VerificationStatus = 2,
            VerifiedByUserId   = @SystemAdminId,
            VerifiedAt         = SYSUTCDATETIME(),
            UpdatedAt          = SYSUTCDATETIME();

        INSERT INTO [dbo].[SupplierBranches]
            (SupplierId, BranchName, ContactName, Email, Phone,
             AddressLine, Province, ShippingDetails, WarrantyInfo,
             IsDefault, CreatedByApplicantId, CreatedAt, UpdatedAt)
        SELECT s.Id,
               N'Sede principal',
               s.ContactName, s.Email, s.Phone,
               s.Location, NULL, s.ShippingDetails, s.WarrantyInfo,
               1, NULL, SYSUTCDATETIME(), SYSUTCDATETIME()
        FROM [dbo].[Suppliers] s;

        UPDATE q
        SET q.SupplierBranchId = b.Id
        FROM [dbo].[Quotations] q
        INNER JOIN [dbo].[SupplierBranches] b
                ON b.SupplierId = q.SupplierId
               AND b.IsDefault = 1
        WHERE q.SupplierBranchId IS NULL OR q.SupplierBranchId = 0;

        IF EXISTS (SELECT 1 FROM [dbo].[Quotations]
                   WHERE SupplierBranchId IS NULL OR SupplierBranchId = 0)
            THROW 50011, 'Migration 013: at least one Quotation has a NULL or 0 SupplierBranchId after backfill.', 1;

        IF EXISTS (
            SELECT 1
            FROM [dbo].[Quotations] q
            INNER JOIN [dbo].[SupplierBranches] b ON q.SupplierBranchId = b.Id
            WHERE q.SupplierId <> b.SupplierId)
            THROW 50012, 'Migration 013: at least one Quotation has SupplierId != Branch.SupplierId.', 1;

        IF EXISTS (
            SELECT s.Id
            FROM [dbo].[Suppliers] s
            LEFT JOIN [dbo].[SupplierBranches] b ON b.SupplierId = s.Id AND b.IsDefault = 1
            WHERE b.Id IS NULL)
            THROW 50013, 'Migration 013: at least one Supplier is missing a default branch.', 1;

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        ;THROW;
    END CATCH;
END;
GO
