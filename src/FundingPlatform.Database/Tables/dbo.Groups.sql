CREATE TABLE [dbo].[Groups]
(
    [Id]        INT             IDENTITY(1,1) NOT NULL,
    [Name]      NVARCHAR(100)   COLLATE Latin1_General_CI_AI NOT NULL,
    -- Spec 021 / data-model.md — every Group lives under a Process. The default
    -- of 0 is a placeholder that the post-deploy seed (02_SeedMigracionInicial)
    -- overwrites with the real "Migración inicial" Process Id on first run.
    -- The FK is added in the same post-deploy script, AFTER the backfill, so
    -- existing seed groups (Norte/Sur/Centro from spec 016) can survive the
    -- forward-only upgrade without violating referential integrity.
    [ProcessId] INT             NOT NULL CONSTRAINT [DF_Groups_ProcessId] DEFAULT (0),
    [CreatedAt] DATETIMEOFFSET  NOT NULL CONSTRAINT [DF_Groups_CreatedAt] DEFAULT (SYSUTCDATETIME()),
    [UpdatedAt] DATETIMEOFFSET  NOT NULL CONSTRAINT [DF_Groups_UpdatedAt] DEFAULT (SYSUTCDATETIME()),

    CONSTRAINT [PK_Groups] PRIMARY KEY CLUSTERED ([Id])
);
GO

CREATE NONCLUSTERED INDEX [IX_Groups_ProcessId]
    ON [dbo].[Groups] ([ProcessId]);
GO

-- Group.Name uniqueness is scoped PER PROCESS (not global): a group name must be unique
-- among the groups of its own Process, but the same name may be reused by a group in a
-- different Process (even within the same Fund). Column collation provides case/accent
-- insensitivity; the composite unique index (ProcessId, Name) is the authoritative gate
-- against duplicates submitted by two admins concurrently.
CREATE UNIQUE NONCLUSTERED INDEX [UX_Groups_ProcessId_Name]
    ON [dbo].[Groups] ([ProcessId], [Name]);
GO
