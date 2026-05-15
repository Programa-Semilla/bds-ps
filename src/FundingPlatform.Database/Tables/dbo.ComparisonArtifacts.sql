-- Spec 020 / FR-D1, FR-D2 — cached AI comparison artifact for a single
-- ApplicationItem. PK is the item id (1:1; replaced in place on regenerate).
-- ApplicationItemId is INT to match dbo.Items.Id; data-model.md drafted the
-- shape against a hypothetical Guid id but the live schema is INT identity.
CREATE TABLE [dbo].[ComparisonArtifacts]
(
    [ApplicationItemId]    INT               NOT NULL,
    [JsonContent]          NVARCHAR(MAX)     NOT NULL,
    [InputHash]            CHAR(64)          NOT NULL,
    [PromptVersion]        NVARCHAR(64)      NOT NULL,
    [SchemaVersion]        NVARCHAR(32)      NOT NULL,
    [AiModel]              NVARCHAR(128)     NOT NULL,
    [GeneratedAt]          DATETIMEOFFSET    NOT NULL,
    [GeneratedByUserId]    NVARCHAR(450)     NOT NULL,
    [TokenCostInput]       INT               NOT NULL,
    [TokenCostOutput]      INT               NOT NULL,
    [LatencyMs]            INT               NOT NULL,

    CONSTRAINT [PK_ComparisonArtifacts]
        PRIMARY KEY CLUSTERED ([ApplicationItemId]),
    CONSTRAINT [FK_ComparisonArtifacts_Items]
        FOREIGN KEY ([ApplicationItemId])
        REFERENCES [dbo].[Items]([Id])
        ON DELETE CASCADE
);
GO
CREATE INDEX [IX_ComparisonArtifacts_InputHash]
    ON [dbo].[ComparisonArtifacts]([InputHash]);
GO
