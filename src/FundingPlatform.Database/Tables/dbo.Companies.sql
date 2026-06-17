-- Spec 037 / FR-001 — admin-managed company (Empresa) owned by exactly one
-- Applicant (one applicant → many companies). Name-only (≤200, matching the
-- Applications.CompanyName snapshot width) + soft-archive lifecycle. The filtered
-- unique index enforces per-applicant uniqueness among ACTIVE companies (D3);
-- accent-insensitivity is provided by the app-level service pre-check.
CREATE TABLE [dbo].[Companies]
(
    [Id]          INT               IDENTITY(1,1) NOT NULL,
    [ApplicantId] INT               NOT NULL,
    [Name]        NVARCHAR(200)     NOT NULL,
    [ArchivedAt]  DATETIMEOFFSET(0) NULL,
    [CreatedAt]   DATETIME2         NOT NULL CONSTRAINT [DF_Companies_CreatedAt] DEFAULT (GETUTCDATE()),
    [UpdatedAt]   DATETIME2         NOT NULL,
    [RowVersion]  ROWVERSION        NOT NULL,

    CONSTRAINT [PK_Companies] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [FK_Companies_Applicants]
        FOREIGN KEY ([ApplicantId]) REFERENCES [dbo].[Applicants]([Id]) ON DELETE NO ACTION
);
GO

CREATE NONCLUSTERED INDEX [IX_Companies_ApplicantId]
    ON [dbo].[Companies]([ApplicantId]);
GO

-- Per-applicant active-name uniqueness backstop (D3). Case-insensitivity comes
-- from the column collation; accent-insensitivity from the service pre-check.
CREATE UNIQUE NONCLUSTERED INDEX [UX_Companies_ApplicantId_Name]
    ON [dbo].[Companies]([ApplicantId],[Name]) WHERE [ArchivedAt] IS NULL;
GO
