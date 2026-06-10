/*
    Post-Deployment include: 00_SeedFunds.sql
    Spec 029 / D10 — bootstrap the seed Fund ("Fondo General", Active) so the
    required Processes.FundId FK can be satisfied when the "Migración inicial"
    Process is seeded (02_SeedMigracionInicialProcess.sql).

    MUST run BEFORE 02_SeedMigracionInicialProcess.sql.

    Idempotent: MERGE on Name. Re-runs are a no-op once the seed Fund exists.
*/

MERGE INTO [dbo].[Funds] AS tgt
USING (VALUES
    (N'Fondo General', N'Fondo general del Programa Semilla.', CAST(0 AS TINYINT))
) AS src ([Name], [Description], [Status])
ON tgt.[Name] = src.[Name]
WHEN NOT MATCHED THEN
    INSERT ([Name], [Description], [Status])
    VALUES (src.[Name], src.[Description], src.[Status]);
GO
