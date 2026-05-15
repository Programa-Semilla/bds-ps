CREATE TABLE [dbo].[Applications]
(
    [Id]                INT            IDENTITY(1,1) NOT NULL,
    [ApplicantId]       INT            NOT NULL,
    [CompanyName]       NVARCHAR(200)  NOT NULL,
    [State]             INT            NOT NULL CONSTRAINT [DF_Applications_State] DEFAULT (0),
    -- Spec 021 / FR-008 — PublicCode is the human-facing identifier displayed
    -- on every surface (dashboards, /review, reviewer queue, signing inbox,
    -- emails, Funding Agreement PDF). Crockford-base32 alphabet (excludes
    -- I, L, O, U, 0, 1) split as 4-4 with a literal hyphen. NFR-001 — no
    -- production data, so no DB-side default / backfill is required; the
    -- domain layer (IPublicCodeGenerator) is the only inserter.
    [PublicCode]        CHAR(9)        NOT NULL,
    -- Spec 021 / data-model.md — nullable until the applicant picks an
    -- ImpactTemplate on first save; the domain guard on Submit() requires
    -- it to be set for any state >= Submitted.
    [ImpactTemplateId]  INT            NULL,
    -- Spec 021 / data-model.md — bitmask: 0x1 = T-72h sent, 0x2 = T-24h sent,
    -- 0x4 = expiry sent. Atomic update by StageExpiryReminderService.
    [RemindersSentMask] TINYINT        NOT NULL CONSTRAINT [DF_Applications_RemindersSentMask] DEFAULT (0),
    -- Spec 021 / data-model.md — reset whenever the application crosses a stage
    -- boundary (Borrador → Submitted, Submitted → InReview, …). Used by the
    -- stage-expiry evaluator and the countdown banner.
    [StageEnteredAt]    DATETIMEOFFSET(0)   NOT NULL CONSTRAINT [DF_Applications_StageEnteredAt] DEFAULT (SYSUTCDATETIME()),
    -- Spec 021 / FR-021 — soft-delete column. Dashboards filter via
    -- IApplicationQueryFilter.ExcludeDeleted; the column is NULL for live rows.
    [DeletedAt]         DATETIMEOFFSET(0)   NULL,
    [CreatedAt]         DATETIME2      NOT NULL CONSTRAINT [DF_Applications_CreatedAt] DEFAULT (GETUTCDATE()),
    [UpdatedAt]         DATETIME2      NOT NULL,
    [SubmittedAt]       DATETIME2      NULL,
    [RowVersion]        ROWVERSION     NOT NULL,

    CONSTRAINT [PK_Applications] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [FK_Applications_Applicants] FOREIGN KEY ([ApplicantId]) REFERENCES [dbo].[Applicants] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_Applications_ImpactTemplates] FOREIGN KEY ([ImpactTemplateId]) REFERENCES [dbo].[ImpactTemplates] ([Id]) ON DELETE NO ACTION,
    -- Spec 021 / FR-008 — Crockford-base32 4-4 with hyphen (alphabet excludes
    -- I, L, O, U, 0, 1). The check matches the regex enforced by the
    -- PublicCode value object on the domain side.
    CONSTRAINT [CK_Applications_PublicCode] CHECK (
        [PublicCode] LIKE '[A-HJ-NP-Z2-9][A-HJ-NP-Z2-9][A-HJ-NP-Z2-9][A-HJ-NP-Z2-9]-[A-HJ-NP-Z2-9][A-HJ-NP-Z2-9][A-HJ-NP-Z2-9][A-HJ-NP-Z2-9]'
    )
);
GO

CREATE NONCLUSTERED INDEX [IX_Applications_ApplicantId]
    ON [dbo].[Applications] ([ApplicantId]);
GO

CREATE NONCLUSTERED INDEX [IX_Applications_State]
    ON [dbo].[Applications] ([State]);
GO

-- Spec 021 / FR-008 — PublicCode is globally unique; the unique index is the
-- authoritative collision guard (the application-layer generator retries on
-- DB collision per research.md R-1).
CREATE UNIQUE NONCLUSTERED INDEX [UX_Applications_PublicCode]
    ON [dbo].[Applications] ([PublicCode]);
GO

-- Covers the soft-delete filter applied to every dashboard projection.
CREATE NONCLUSTERED INDEX [IX_Applications_DeletedAt]
    ON [dbo].[Applications] ([DeletedAt]);
GO
