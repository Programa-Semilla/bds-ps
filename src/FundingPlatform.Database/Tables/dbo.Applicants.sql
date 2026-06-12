CREATE TABLE [dbo].[Applicants]
(
    [Id]               INT            IDENTITY(1,1) NOT NULL,
    [UserId]           NVARCHAR(450)  NOT NULL,
    [LegalId]          NVARCHAR(50)   NOT NULL,
    [IdentificationType] TINYINT      NULL,            -- spec 026; NULL = unassigned (legacy / non-applicant-role admin user)
    [FirstName]        NVARCHAR(100)  NOT NULL,
    [LastName]         NVARCHAR(100)  NOT NULL,
    [Email]            NVARCHAR(256)  NOT NULL,
    [Phone]            NVARCHAR(20)   NULL,
    [PerformanceScore] DECIMAL(5,2)   NULL,
    [UserCode]         NVARCHAR(50)   NULL,            -- spec 032; admin-assigned free-text code, required for Solicitante at the use-case boundary, NULL = unassigned (legacy)
    [CreatedAt]        DATETIME2      NOT NULL CONSTRAINT [DF_Applicants_CreatedAt] DEFAULT (GETUTCDATE()),
    [UpdatedAt]        DATETIME2      NOT NULL,

    CONSTRAINT [PK_Applicants] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [UX_Applicants_UserId] UNIQUE ([UserId]),
    CONSTRAINT [UX_Applicants_LegalId] UNIQUE ([LegalId]),
    CONSTRAINT [FK_Applicants_AspNetUsers] FOREIGN KEY ([UserId]) REFERENCES [dbo].[AspNetUsers] ([Id]) ON DELETE NO ACTION
);
GO

-- Spec 032 — unique among assigned User Codes; filtered so any number of
-- code-less applicants (legacy / not-yet-assigned) coexist.
CREATE UNIQUE NONCLUSTERED INDEX [UX_Applicants_UserCode]
    ON [dbo].[Applicants] ([UserCode]) WHERE [UserCode] IS NOT NULL;
GO
