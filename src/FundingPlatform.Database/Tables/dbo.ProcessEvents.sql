-- Spec 044 — see specs/044-process-reception-windows/data-model.md.
-- A general per-Process calendar item. The ReceptionWindow type (EventType=0,
-- ControlsSubmissionAvailability=1) gates submission availability; other types
-- are reserved (US5, schema-only). CK_EndAfterStart is a defense-in-depth
-- backstop; the user-facing es-CR rejection (FR-003) is enforced in the domain.
CREATE TABLE [dbo].[ProcessEvents]
(
    [Id]                             INT               IDENTITY(1,1) NOT NULL,
    [ProcessId]                      INT               NOT NULL,
    [EventType]                      TINYINT           NOT NULL CONSTRAINT [DF_ProcessEvents_EventType] DEFAULT (0),
    [Name]                           NVARCHAR(120)     NOT NULL,
    [Description]                    NVARCHAR(500)     NULL,
    [StartUtc]                       DATETIMEOFFSET(0) NOT NULL,
    [EndUtc]                         DATETIMEOFFSET(0) NOT NULL,
    [ControlsSubmissionAvailability] BIT               NOT NULL CONSTRAINT [DF_ProcessEvents_Controls] DEFAULT (0),
    [ApplicantFacingMessage]         NVARCHAR(500)     NULL,
    [IsActive]                       BIT               NOT NULL CONSTRAINT [DF_ProcessEvents_IsActive] DEFAULT (1),
    [DisplayOrder]                   INT               NOT NULL CONSTRAINT [DF_ProcessEvents_DisplayOrder] DEFAULT (0),
    [CreatedAt]                      DATETIMEOFFSET(0) NOT NULL CONSTRAINT [DF_ProcessEvents_CreatedAt] DEFAULT (SYSUTCDATETIME()),
    [CreatedByUserId]                NVARCHAR(450)     NULL,
    [UpdatedAt]                      DATETIMEOFFSET(0) NULL,
    [UpdatedByUserId]                NVARCHAR(450)     NULL,
    [RowVersion]                     ROWVERSION        NOT NULL,
    CONSTRAINT [PK_ProcessEvents] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [FK_ProcessEvents_Processes]
        FOREIGN KEY ([ProcessId]) REFERENCES [dbo].[Processes]([Id]) ON DELETE NO ACTION,
    CONSTRAINT [CK_ProcessEvents_EndAfterStart] CHECK ([EndUtc] > [StartUtc])
);
GO
CREATE NONCLUSTERED INDEX [IX_ProcessEvents_ProcessId]
    ON [dbo].[ProcessEvents] ([ProcessId]) INCLUDE ([IsActive], [EventType]);
