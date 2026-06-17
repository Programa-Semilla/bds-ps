CREATE TABLE [dbo].[ItemImpacts]
(
    [Id]                  INT IDENTITY (1, 1) NOT NULL,
    -- Spec 035 (evolved 2026-06-16, data-model.md D14) — per-item impact ATTRIBUTION:
    -- a line item supports one-or-more of the application's declared impacts.
    [ItemId]              INT NOT NULL,
    [ApplicationImpactId] INT NOT NULL,

    CONSTRAINT [PK_ItemImpacts] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [FK_ItemImpacts_Items]
        FOREIGN KEY ([ItemId]) REFERENCES [dbo].[Items] ([Id]) ON DELETE CASCADE,
    -- NO ACTION (not CASCADE) is deliberate: [Application] reaches [ItemImpacts] via two
    -- paths — Application->Items->ItemImpacts AND Application->ApplicationImpacts->ItemImpacts.
    -- SQL Server forbids multiple cascade paths to one table. Deleting a declared impact is
    -- therefore handled in the domain (Application.RemoveImpact strips the referencing
    -- ItemImpact rows first, SC-007) rather than by the database.
    CONSTRAINT [FK_ItemImpacts_ApplicationImpacts]
        FOREIGN KEY ([ApplicationImpactId]) REFERENCES [dbo].[ApplicationImpacts] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [UX_ItemImpacts_ItemId_AppImpactId] UNIQUE ([ItemId], [ApplicationImpactId])
);
GO

CREATE NONCLUSTERED INDEX [IX_ItemImpacts_ApplicationImpactId]
    ON [dbo].[ItemImpacts] ([ApplicationImpactId]);
GO
