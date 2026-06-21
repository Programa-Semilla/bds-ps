-- Spec 040 / D6 / D13 — the recorded outcome of one checklist item against one
-- application at one stage. ItemTextSnapshot freezes the item text at completion
-- (FR-003) so later template edits never rewrite recorded responses. Both FKs are
-- NO ACTION: applications are soft-deleted and template items are deactivated (never
-- hard-deleted), so history survives and no multiple-cascade-path publish failure can
-- occur (the spec-029/035/036 lesson). One current row per
-- (ApplicationId, Stage, ChecklistTemplateItemId), overwritten each completion cycle.
-- See specs/040-auditor-workflow-stage/data-model.md.
CREATE TABLE [dbo].[ApplicationChecklistResponses]
(
    [Id]                      INT             IDENTITY(1,1) NOT NULL,
    [ApplicationId]           INT             NOT NULL,
    -- ChecklistStage: 1=Reviewer, 2=Auditor (never 3/Both on a response)
    [Stage]                   TINYINT         NOT NULL,
    [ChecklistTemplateItemId] INT             NOT NULL,
    [ItemTextSnapshot]        NVARCHAR(500)   NOT NULL,
    -- ChecklistResponseStatus: 1=Checked, 2=NotCompliant
    [Status]                  TINYINT         NOT NULL,
    [NonComplianceReason]     NVARCHAR(1000)  NULL,
    [CompletedByUserId]       NVARCHAR(450)   NOT NULL,
    [CompletedAtUtc]          DATETIME2(3)    NOT NULL CONSTRAINT [DF_ApplicationChecklistResponses_CompletedAtUtc] DEFAULT (GETUTCDATE()),
    [RowVersion]              ROWVERSION      NOT NULL,

    CONSTRAINT [PK_ApplicationChecklistResponses] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [FK_ApplicationChecklistResponses_Applications]
        FOREIGN KEY ([ApplicationId]) REFERENCES [dbo].[Applications]([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_ApplicationChecklistResponses_ChecklistTemplateItems]
        FOREIGN KEY ([ChecklistTemplateItemId]) REFERENCES [dbo].[ChecklistTemplateItems]([Id]) ON DELETE NO ACTION,
    CONSTRAINT [CK_ApplicationChecklistResponses_Stage] CHECK ([Stage] IN (1, 2)),
    CONSTRAINT [CK_ApplicationChecklistResponses_Status] CHECK ([Status] IN (1, 2))
);
GO

-- Spec 040 — one current response row per (application, stage, item). Enforced so a
-- concurrent duplicate-insert race (two auditors saving the same application) fails as a
-- unique violation (→ stale-state refusal) instead of accumulating duplicate rows.
CREATE UNIQUE NONCLUSTERED INDEX [UX_ApplicationChecklistResponses_App_Stage_Item]
    ON [dbo].[ApplicationChecklistResponses] ([ApplicationId], [Stage], [ChecklistTemplateItemId]);
GO

CREATE NONCLUSTERED INDEX [IX_ApplicationChecklistResponses_ApplicationId_Stage]
    ON [dbo].[ApplicationChecklistResponses] ([ApplicationId], [Stage]);
GO

CREATE NONCLUSTERED INDEX [IX_ApplicationChecklistResponses_ChecklistTemplateItemId]
    ON [dbo].[ApplicationChecklistResponses] ([ChecklistTemplateItemId]);
GO
