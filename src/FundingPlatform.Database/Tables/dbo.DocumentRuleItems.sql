-- Spec 047 — one row of a DocumentRuleSet: whether a given EvidenceType is required. EvidenceType
-- is a TINYINT enum (EF HasConversion<byte>()). One row per (set, type), enforced by the UNIQUE.
-- Edits are full-replace (no snapshot table needed — completeness is live, closure is stored).
-- FK to the parent set is CASCADE. See specs/047-evidence-graph-required-docs/data-model.md.
CREATE TABLE [dbo].[DocumentRuleItems]
(
    [Id]                 INT     IDENTITY(1,1) NOT NULL,
    [DocumentRuleSetId]  INT     NOT NULL,
    [EvidenceType]       TINYINT NOT NULL,
    [IsRequired]         BIT     NOT NULL,

    CONSTRAINT [PK_DocumentRuleItems] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [FK_DocumentRuleItems_DocumentRuleSets]
        FOREIGN KEY ([DocumentRuleSetId]) REFERENCES [dbo].[DocumentRuleSets]([Id]) ON DELETE CASCADE,
    CONSTRAINT [UX_DocumentRuleItems_Set_Type] UNIQUE ([DocumentRuleSetId], [EvidenceType])
);
GO
