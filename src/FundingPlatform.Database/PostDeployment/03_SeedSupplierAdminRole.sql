/*
    Post-Deployment include: 03_SeedSupplierAdminRole.sql
    Spec 038 / D1 — Rename the legacy SupplierAdmin role to Auditor.

    The role name lives only in AspNetRoles.Name/NormalizedName (no schema/FK
    depends on the string), so renaming the existing row carries all
    AspNetUserRoles memberships over with zero migration. Idempotent and safe to
    re-run every deploy:
      - If a SUPPLIERADMIN row exists  -> rename it in place to Auditor.
      - Else if no AUDITOR row exists   -> insert a fresh Auditor role.
      - Else                            -> no-op (already renamed).
*/

IF EXISTS (SELECT 1 FROM [dbo].[AspNetRoles] WHERE [NormalizedName] = N'SUPPLIERADMIN')
    UPDATE [dbo].[AspNetRoles]
       SET [Name] = N'Auditor', [NormalizedName] = N'AUDITOR'
     WHERE [NormalizedName] = N'SUPPLIERADMIN';
ELSE IF NOT EXISTS (SELECT 1 FROM [dbo].[AspNetRoles] WHERE [NormalizedName] = N'AUDITOR')
    INSERT INTO [dbo].[AspNetRoles] ([Id], [Name], [NormalizedName], [ConcurrencyStamp])
    VALUES (NEWID(), N'Auditor', N'AUDITOR', NEWID());
GO
