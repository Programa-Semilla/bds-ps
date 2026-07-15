/*
    Post-Deployment include: 09_DropSolicitudWindowDays.sql
    Spec 044 — drop the legacy [dbo].[Processes].[SolicitudWindowDays] column
    (removed from the dacpac model). Reception windows (dbo.ProcessEvents) replace
    the per-Process Solicitud duration submission gate.

    Dev/E2E deploys drop it automatically via DropObjectsNotInSource=true, but the
    Azure publish uses --no-drop (DropObjectsNotInSource=false), which would leave
    it as an orphaned column and drift dev vs prod. This script drops it
    explicitly. SolicitudWindowDays is nullable with no default constraint, so
    there is no DF_ to drop first. Idempotent + guarded (no-op once removed) — safe
    to re-run on every deploy and in both environments.

    Also removes the now-orphaned platform-default config row.
*/

IF COL_LENGTH('dbo.Processes', 'SolicitudWindowDays') IS NOT NULL
BEGIN
    ALTER TABLE [dbo].[Processes] DROP COLUMN [SolicitudWindowDays];
END
GO

DELETE FROM [dbo].[SystemConfigurations] WHERE [Key] = N'Stage.Solicitud.WindowDays';
GO
