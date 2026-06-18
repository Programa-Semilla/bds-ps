-- Spec 040 / D4–D5 / D13 — admin-configured checklist applied at a workflow stage
-- (Reviewer | Auditor | Both). Owns ordered ChecklistTemplateItems. At most one active
-- template per effective stage is enforced in the admin service. Greenfield additive
-- table. See specs/040-auditor-workflow-stage/data-model.md.
CREATE TABLE [dbo].[ChecklistTemplates]
(
    [Id]              INT            IDENTITY(1,1) NOT NULL,
    [Name]            NVARCHAR(200)  NOT NULL,
    [Description]     NVARCHAR(500)  NULL,
    -- ChecklistStage: 1=Reviewer, 2=Auditor, 3=Both
    [AppliesToStage]  TINYINT        NOT NULL,
    [IsActive]        BIT            NOT NULL CONSTRAINT [DF_ChecklistTemplates_IsActive] DEFAULT (0),
    [CreatedAtUtc]    DATETIME2(3)   NOT NULL CONSTRAINT [DF_ChecklistTemplates_CreatedAtUtc] DEFAULT (GETUTCDATE()),
    [CreatedByUserId] NVARCHAR(450)  NOT NULL,
    [RowVersion]      ROWVERSION     NOT NULL,

    CONSTRAINT [PK_ChecklistTemplates] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [CK_ChecklistTemplates_AppliesToStage] CHECK ([AppliesToStage] IN (1, 2, 3))
);
GO
