/*
    Post-Deployment include: 03_SeedSupplierAdminRole.sql
    Spec 021 / FR-007 — Seed the SupplierAdmin Identity role.

    Mirrors the existing seed style for Applicant / Admin / Reviewer roles (NEWID()
    for Id + ConcurrencyStamp). Idempotent on NormalizedName.
*/

IF NOT EXISTS (SELECT 1 FROM [dbo].[AspNetRoles] WHERE [NormalizedName] = N'SUPPLIERADMIN')
    INSERT INTO [dbo].[AspNetRoles] ([Id], [Name], [NormalizedName], [ConcurrencyStamp])
    VALUES (NEWID(), N'SupplierAdmin', N'SUPPLIERADMIN', NEWID());
GO
