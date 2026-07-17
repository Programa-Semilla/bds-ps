-- Spec 047 — the admin required-document rule matrix (research D5): one rule set per Category
-- (+ one global-default row where CategoryId IS NULL). SQL Server's UNIQUE treats the single NULL
-- as unique-eligible, so UNIQUE (CategoryId) yields at most one global-default row + distinct
-- per-category rows. No response-snapshot table (completeness is live, closure is stored).
-- See specs/047-evidence-graph-required-docs/data-model.md.
CREATE TABLE [dbo].[DocumentRuleSets]
(
    [Id]         INT        IDENTITY(1,1) NOT NULL,
    [CategoryId] INT        NULL,
    [RowVersion] ROWVERSION NOT NULL,

    CONSTRAINT [PK_DocumentRuleSets] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [FK_DocumentRuleSets_Categories]
        FOREIGN KEY ([CategoryId]) REFERENCES [dbo].[Categories]([Id]) ON DELETE NO ACTION,
    CONSTRAINT [UX_DocumentRuleSets_CategoryId] UNIQUE ([CategoryId])
);
GO
