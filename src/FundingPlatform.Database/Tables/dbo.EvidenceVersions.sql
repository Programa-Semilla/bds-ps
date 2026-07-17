-- Spec 047 — the append-only version chain for an Evidence node (research D4). Each replace (of
-- the file OR a reconciliation-critical field) appends a new current row and marks the prior
-- superseded — rows never mutate except IsCurrent 1→0. Exactly one current per evidence, enforced
-- by the filtered unique UX_EvidenceVersions_OneCurrent (copies UX_SignedUploads_OnePending).
-- Reason is required for versions after the first (enforced in the domain). FK to Evidence is
-- CASCADE (owned child). See specs/047-evidence-graph-required-docs/data-model.md.
CREATE TABLE [dbo].[EvidenceVersions]
(
    [Id]                      INT               IDENTITY(1,1) NOT NULL,
    [EvidenceId]              INT               NOT NULL,
    [VersionNumber]           INT               NOT NULL,
    [IsCurrent]               BIT               NOT NULL,
    [BlobKey]                 NVARCHAR(1024)    NOT NULL,
    [OriginalFileName]        NVARCHAR(500)     NOT NULL,
    [FileSize]                BIGINT            NOT NULL,
    [ContentType]             NVARCHAR(100)     NOT NULL,
    [Amount]                  DECIMAL(18,2)     NOT NULL,
    [Currency]                CHAR(3)           NOT NULL,
    [DocumentReferenceNumber] NVARCHAR(100)     NOT NULL,
    [DocumentDate]            DATE              NOT NULL,
    [FileHash]                CHAR(64)          NOT NULL,
    [Reason]                  NVARCHAR(500)     NULL,
    [CreatedByUserId]         NVARCHAR(450)     NOT NULL,
    [CreatedAtUtc]            DATETIMEOFFSET(0)  NOT NULL CONSTRAINT [DF_EvidenceVersions_CreatedAtUtc] DEFAULT (SYSUTCDATETIME()),

    CONSTRAINT [PK_EvidenceVersions] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [FK_EvidenceVersions_Evidence]
        FOREIGN KEY ([EvidenceId]) REFERENCES [dbo].[Evidence]([Id]) ON DELETE CASCADE,
    CONSTRAINT [FK_EvidenceVersions_AspNetUsers]
        FOREIGN KEY ([CreatedByUserId]) REFERENCES [dbo].[AspNetUsers]([Id]) ON DELETE NO ACTION,
    CONSTRAINT [CK_EvidenceVersions_Amount_Positive] CHECK ([Amount] > 0),
    CONSTRAINT [CK_EvidenceVersions_FileSize_Positive] CHECK ([FileSize] > 0)
);
GO

CREATE NONCLUSTERED INDEX [IX_EvidenceVersions_EvidenceId]
    ON [dbo].[EvidenceVersions] ([EvidenceId]);
GO

-- Exactly one current version per evidence.
CREATE UNIQUE NONCLUSTERED INDEX [UX_EvidenceVersions_OneCurrent]
    ON [dbo].[EvidenceVersions] ([EvidenceId])
    WHERE [IsCurrent] = 1;
GO
