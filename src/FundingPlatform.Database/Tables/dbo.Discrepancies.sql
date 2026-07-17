-- Spec 048 — the persisted, stateful reconciliation discrepancy. Turns the P1–P3 ephemeral,
-- computed-on-read hard blocks into a durable row with a fixed per-rule severity (Blocking / non-
-- blocking Warning) and a lifecycle (Open→Assigned→UnderCorrection→Resolved|Waived). The row is
-- engine-managed (only the materializer inserts/refreshes/auto-resolves it); it exists for
-- visibility + lifecycle. The money gates keep recomputing fresh and throwing (persistence model C,
-- SC-004). ScopeType/Comparison/Severity/State are TINYINT enums (EF HasConversion<byte>()); the four
-- money columns are exact DECIMAL(18,2). ScopeEntityId is polymorphic (no FK — research D2). FK to
-- Applications is NO ACTION (soft-delete filter model). See specs/048-full-reconciliation-engine/data-model.md.
CREATE TABLE [dbo].[Discrepancies]
(
    [Id]               INT               IDENTITY(1,1) NOT NULL,
    [ApplicationId]    INT               NOT NULL,
    [ScopeType]        TINYINT           NOT NULL,
    [ScopeEntityId]    INT               NOT NULL,
    [Comparison]       TINYINT           NOT NULL,
    [Severity]         TINYINT           NOT NULL,
    [State]            TINYINT           NOT NULL CONSTRAINT [DF_Discrepancies_State] DEFAULT (0),
    [Expected]         DECIMAL(18,2)     NOT NULL,
    [Actual]           DECIMAL(18,2)     NOT NULL,
    [Difference]       DECIMAL(18,2)     NOT NULL,
    [ToleranceApplied] DECIMAL(18,2)     NOT NULL CONSTRAINT [DF_Discrepancies_Tolerance] DEFAULT (0),
    [SourceDocument]   NVARCHAR(200)     NOT NULL,
    [AssigneeUserId]   NVARCHAR(450)     NULL,
    [FirstDetectedAt]  DATETIMEOFFSET(0)  NOT NULL CONSTRAINT [DF_Discrepancies_FirstDetectedAt] DEFAULT (SYSUTCDATETIME()),
    [LastEvaluatedAt]  DATETIMEOFFSET(0)  NOT NULL CONSTRAINT [DF_Discrepancies_LastEvaluatedAt] DEFAULT (SYSUTCDATETIME()),
    [ResolvedAt]       DATETIMEOFFSET(0)  NULL,
    [WaivedReason]     NVARCHAR(500)     NULL,
    [RowVersion]       ROWVERSION        NOT NULL,

    CONSTRAINT [PK_Discrepancies] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [FK_Discrepancies_Applications]
        FOREIGN KEY ([ApplicationId]) REFERENCES [dbo].[Applications]([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_Discrepancies_AspNetUsers_Assignee]
        FOREIGN KEY ([AssigneeUserId]) REFERENCES [dbo].[AspNetUsers]([Id]) ON DELETE NO ACTION,
    -- A Blocking discrepancy (Severity=0) can never be Waived (State=4) — DB backstop of the domain guard.
    CONSTRAINT [CK_Discrepancies_Waive_Blocking] CHECK (NOT ([Severity] = 0 AND [State] = 4)),
    -- A Waived row must carry a reason.
    CONSTRAINT [CK_Discrepancies_WaivedReason] CHECK ([State] <> 4 OR [WaivedReason] IS NOT NULL)
);
GO

-- FR-003 — exactly one row per stable identity, ever.
CREATE UNIQUE NONCLUSTERED INDEX [UX_Discrepancies_Identity]
    ON [dbo].[Discrepancies] ([ApplicationId], [ScopeType], [ScopeEntityId], [Comparison]);
GO

-- Dashboard / money-gate reads by application + lifecycle state.
CREATE NONCLUSTERED INDEX [IX_Discrepancies_App_State]
    ON [dbo].[Discrepancies] ([ApplicationId], [State]) INCLUDE ([Severity]);
GO

-- Filter by responsible user.
CREATE NONCLUSTERED INDEX [IX_Discrepancies_Assignee]
    ON [dbo].[Discrepancies] ([AssigneeUserId])
    WHERE [AssigneeUserId] IS NOT NULL;
GO
