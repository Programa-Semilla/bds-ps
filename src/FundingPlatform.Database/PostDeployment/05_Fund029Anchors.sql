/*
    Post-Deployment include: 05_Fund029Anchors.sql
    Spec 029 — migration-safe finalization of the Fund anchors. Runs LAST (after
    Funds, Processes, and Groups are all seeded/reconciled).

    The Processes.FundId and Applications.GroupId columns are declared NOT NULL
    with a DEFAULT(0) placeholder so they can be added to already-populated
    tables (existing dev/staging DBs) without failing the publish. This script:
      1. backfills any placeholder/invalid FundId on existing Processes to the
         seed Fund, then adds FK_Processes_Funds;
      2. backfills any placeholder/invalid GroupId on existing Applications to a
         valid Group under an Active Process+Fund, then adds FK_Applications_Groups.

    On a fresh database the backfills touch nothing (the seeds already set real
    values, and no Applications exist) and the FKs are simply created.
    Idempotent: re-runs find the FKs present and short-circuit.
*/

-- =============================================================================
-- 1. Processes.FundId → seed Fund for any placeholder/invalid value.
-- =============================================================================
DECLARE @FundId INT = (
    SELECT TOP 1 [Id] FROM [dbo].[Funds] WHERE [Name] = N'Fondo General' ORDER BY [Id]
);

IF @FundId IS NOT NULL
BEGIN
    UPDATE [dbo].[Processes]
    SET    [FundId] = @FundId
    WHERE  [FundId] NOT IN (SELECT [Id] FROM [dbo].[Funds]);
END;

-- 2. FK_Processes_Funds (idempotent; safe now that every FundId is valid).
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_Processes_Funds')
   AND NOT EXISTS (SELECT 1 FROM [dbo].[Processes] WHERE [FundId] NOT IN (SELECT [Id] FROM [dbo].[Funds]))
BEGIN
    ALTER TABLE [dbo].[Processes] WITH CHECK
        ADD CONSTRAINT [FK_Processes_Funds]
            FOREIGN KEY ([FundId]) REFERENCES [dbo].[Funds] ([Id]) ON DELETE NO ACTION;
END;

-- =============================================================================
-- 3. Applications.GroupId → a valid seed Group (under an Active Process+Fund so
--    backfilled applications are not accidentally frozen) for any placeholder/
--    invalid value.
-- =============================================================================
DECLARE @GroupId INT = (
    SELECT TOP 1 g.[Id]
    FROM [dbo].[Groups] g
    INNER JOIN [dbo].[Processes] p ON p.[Id] = g.[ProcessId]
    INNER JOIN [dbo].[Funds] f ON f.[Id] = p.[FundId]
    WHERE p.[Status] = 0 AND f.[Status] = 0
    ORDER BY g.[Id]
);

IF @GroupId IS NOT NULL
BEGIN
    UPDATE [dbo].[Applications]
    SET    [GroupId] = @GroupId
    WHERE  [GroupId] NOT IN (SELECT [Id] FROM [dbo].[Groups]);
END;

-- 4. FK_Applications_Groups (idempotent; only when no orphan GroupId remains).
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_Applications_Groups')
   AND NOT EXISTS (SELECT 1 FROM [dbo].[Applications] WHERE [GroupId] NOT IN (SELECT [Id] FROM [dbo].[Groups]))
BEGIN
    ALTER TABLE [dbo].[Applications] WITH CHECK
        ADD CONSTRAINT [FK_Applications_Groups]
            FOREIGN KEY ([GroupId]) REFERENCES [dbo].[Groups] ([Id]) ON DELETE NO ACTION;
END;
GO
