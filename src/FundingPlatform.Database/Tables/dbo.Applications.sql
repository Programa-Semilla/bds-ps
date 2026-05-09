CREATE TABLE [dbo].[Applications]
(
    [Id]          INT            IDENTITY(1,1) NOT NULL,
    [ApplicantId] INT            NOT NULL,
    [CompanyName] NVARCHAR(200)  NOT NULL,
    [State]       INT            NOT NULL CONSTRAINT [DF_Applications_State] DEFAULT (0),
    [CreatedAt]   DATETIME2      NOT NULL CONSTRAINT [DF_Applications_CreatedAt] DEFAULT (GETUTCDATE()),
    [UpdatedAt]   DATETIME2      NOT NULL,
    [SubmittedAt] DATETIME2      NULL,
    [RowVersion]  ROWVERSION     NOT NULL,

    CONSTRAINT [PK_Applications] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [FK_Applications_Applicants] FOREIGN KEY ([ApplicantId]) REFERENCES [dbo].[Applicants] ([Id]) ON DELETE NO ACTION
);
GO

CREATE NONCLUSTERED INDEX [IX_Applications_ApplicantId]
    ON [dbo].[Applications] ([ApplicantId]);
GO

CREATE NONCLUSTERED INDEX [IX_Applications_State]
    ON [dbo].[Applications] ([State]);
GO
