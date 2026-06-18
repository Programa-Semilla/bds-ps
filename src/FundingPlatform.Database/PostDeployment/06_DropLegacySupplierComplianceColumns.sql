/*
    Post-Deployment include: 06_DropLegacySupplierComplianceColumns.sql
    Spec 038 — drop the four legacy BIT compliance/e-invoice columns that were
    removed from the dacpac model (replaced by enumerated regulatory statuses).

    Dev/E2E deploys drop them automatically via DropObjectsNotInSource=true, but
    the Azure publish uses --no-drop (DropObjectsNotInSource=false), which would
    leave these as orphaned NOT NULL columns and drift dev vs prod. This script
    drops them explicitly. Idempotent + guarded (no-op once already removed), so
    it is safe to re-run on every deploy and in both environments.
*/

IF COL_LENGTH('dbo.Suppliers', 'HasElectronicInvoice') IS NOT NULL
BEGIN
    IF EXISTS (SELECT 1 FROM sys.default_constraints WHERE [name] = 'DF_Suppliers_HasElectronicInvoice')
        ALTER TABLE [dbo].[Suppliers] DROP CONSTRAINT [DF_Suppliers_HasElectronicInvoice];
    ALTER TABLE [dbo].[Suppliers] DROP COLUMN [HasElectronicInvoice];
END
GO

IF COL_LENGTH('dbo.Suppliers', 'IsCompliantCCSS') IS NOT NULL
BEGIN
    IF EXISTS (SELECT 1 FROM sys.default_constraints WHERE [name] = 'DF_Suppliers_IsCompliantCCSS')
        ALTER TABLE [dbo].[Suppliers] DROP CONSTRAINT [DF_Suppliers_IsCompliantCCSS];
    ALTER TABLE [dbo].[Suppliers] DROP COLUMN [IsCompliantCCSS];
END
GO

IF COL_LENGTH('dbo.Suppliers', 'IsCompliantHacienda') IS NOT NULL
BEGIN
    IF EXISTS (SELECT 1 FROM sys.default_constraints WHERE [name] = 'DF_Suppliers_IsCompliantHacienda')
        ALTER TABLE [dbo].[Suppliers] DROP CONSTRAINT [DF_Suppliers_IsCompliantHacienda];
    ALTER TABLE [dbo].[Suppliers] DROP COLUMN [IsCompliantHacienda];
END
GO

IF COL_LENGTH('dbo.Suppliers', 'IsCompliantSICOP') IS NOT NULL
BEGIN
    IF EXISTS (SELECT 1 FROM sys.default_constraints WHERE [name] = 'DF_Suppliers_IsCompliantSICOP')
        ALTER TABLE [dbo].[Suppliers] DROP CONSTRAINT [DF_Suppliers_IsCompliantSICOP];
    ALTER TABLE [dbo].[Suppliers] DROP COLUMN [IsCompliantSICOP];
END
GO
