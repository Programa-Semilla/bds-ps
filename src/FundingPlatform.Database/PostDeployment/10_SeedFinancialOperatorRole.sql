/*
    Post-Deployment include: 10_SeedFinancialOperatorRole.sql
    Spec 045 — seed the group-scoped "Financial Operator" Identity role.

    The role name lives only in AspNetRoles.Name/NormalizedName (no schema/FK
    depends on the string). Idempotent and safe to re-run every deploy.
*/

IF NOT EXISTS (SELECT 1 FROM [dbo].[AspNetRoles] WHERE [NormalizedName] = N'FINANCIAL OPERATOR')
    INSERT INTO [dbo].[AspNetRoles] ([Id], [Name], [NormalizedName], [ConcurrencyStamp])
    VALUES (NEWID(), N'Financial Operator', N'FINANCIAL OPERATOR', NEWID());
GO
