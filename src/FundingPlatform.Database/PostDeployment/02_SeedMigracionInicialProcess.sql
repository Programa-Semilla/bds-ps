/*
    Post-Deployment include: 02_SeedMigracionInicialProcess.sql
    Spec 021 / data-model.md — Bootstrap the "Migración inicial" Process row and
    reassign every pre-existing Group to it.

    Idempotent: re-runs MERGE on Name and only updates Groups still pointing at the
    placeholder Id = 0 (the default declared on dbo.Groups.ProcessId).

    Also adds the Groups -> Processes FK once the reconciliation has run. The FK is
    NOT declared on the table because the table-creation path runs before this seed,
    and seed Groups (Norte/Sur/Centro from spec 016) need to land first with the
    placeholder ProcessId = 0.
*/

-- =============================================================================
-- 1. Seed the "Migración inicial" Process row (idempotent MERGE).
-- =============================================================================
MERGE INTO [dbo].[Processes] AS tgt
USING (VALUES
    (N'Migración inicial', CAST(0 AS TINYINT))
) AS src ([Name], [Status])
ON tgt.[Name] = src.[Name]
WHEN NOT MATCHED THEN
    INSERT ([Name], [Status]) VALUES (src.[Name], src.[Status]);

DECLARE @MigracionInicialId INT = (
    SELECT [Id] FROM [dbo].[Processes] WHERE [Name] = N'Migración inicial'
);

-- =============================================================================
-- 2. Reassign every Group whose ProcessId is still the placeholder.
-- =============================================================================
UPDATE [dbo].[Groups]
SET    [ProcessId] = @MigracionInicialId,
       [UpdatedAt] = SYSUTCDATETIME()
WHERE  [ProcessId] = 0
   OR  [ProcessId] IS NULL;

-- =============================================================================
-- 3. Add the FK Groups -> Processes once Groups.ProcessId is fully reconciled.
--    Idempotent: ALTER TABLE … ADD CONSTRAINT only runs when the FK is absent.
-- =============================================================================
IF NOT EXISTS (
    SELECT 1
    FROM sys.foreign_keys
    WHERE name = N'FK_Groups_Processes'
)
BEGIN
    ALTER TABLE [dbo].[Groups] WITH CHECK
        ADD CONSTRAINT [FK_Groups_Processes]
            FOREIGN KEY ([ProcessId]) REFERENCES [dbo].[Processes] ([Id]) ON DELETE NO ACTION;
END;
GO
