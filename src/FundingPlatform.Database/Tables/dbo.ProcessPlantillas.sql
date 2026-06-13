CREATE TABLE [dbo].[ProcessPlantillas]
(
    [Id]                         INT             IDENTITY(1,1) NOT NULL,
    [ProcessId]                  INT             NOT NULL,
    [SourcePlantillaId]          INT             NOT NULL,
    [MinimumQuotationsPerItem]   INT             NOT NULL,
    [RequiredFieldFlags]         BIGINT          NOT NULL,
    -- Spec 035 / D4 — ImpactTemplateIdsCsv dropped: the Plantilla no longer gates
    -- impact-template selection (per-item impact picks any active template).
    [AssignedAt]                 DATETIMEOFFSET(0)    NOT NULL CONSTRAINT [DF_ProcessPlantillas_AssignedAt] DEFAULT (SYSUTCDATETIME()),

    CONSTRAINT [PK_ProcessPlantillas] PRIMARY KEY CLUSTERED ([Id]),
    -- Spec 021 / OQ-1 — exactly one Plantilla snapshot per Process.
    CONSTRAINT [UX_ProcessPlantillas_ProcessId] UNIQUE ([ProcessId]),
    CONSTRAINT [FK_ProcessPlantillas_Processes]
        FOREIGN KEY ([ProcessId]) REFERENCES [dbo].[Processes] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_ProcessPlantillas_Plantillas_Source]
        FOREIGN KEY ([SourcePlantillaId]) REFERENCES [dbo].[Plantillas] ([Id]) ON DELETE NO ACTION
);
GO
