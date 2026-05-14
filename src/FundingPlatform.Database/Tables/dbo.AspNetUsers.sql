CREATE TABLE [dbo].[AspNetUsers]
(
    [Id]                   NVARCHAR(450)  NOT NULL,
    [UserName]             NVARCHAR(256)  NULL,
    [NormalizedUserName]   NVARCHAR(256)  NULL,
    [Email]                NVARCHAR(256)  NULL,
    [NormalizedEmail]      NVARCHAR(256)  NULL,
    [EmailConfirmed]       BIT            NOT NULL,
    [PasswordHash]         NVARCHAR(MAX)  NULL,
    [SecurityStamp]        NVARCHAR(MAX)  NULL,
    [ConcurrencyStamp]     NVARCHAR(MAX)  NULL,
    [PhoneNumber]          NVARCHAR(MAX)  NULL,
    [PhoneNumberConfirmed] BIT            NOT NULL,
    [TwoFactorEnabled]     BIT            NOT NULL,
    [LockoutEnd]           DATETIMEOFFSET NULL,
    [LockoutEnabled]       BIT            NOT NULL,
    [AccessFailedCount]    INT            NOT NULL,
    [FirstName]            NVARCHAR(100)  NULL,
    [LastName]             NVARCHAR(100)  NULL,
    [IsSystemSentinel]     BIT            NOT NULL CONSTRAINT [DF_AspNetUsers_IsSystemSentinel] DEFAULT (0),
    [MustChangePassword]   BIT            NOT NULL CONSTRAINT [DF_AspNetUsers_MustChangePassword] DEFAULT (0),
    -- Spec 021 / FR-031 — admin-set personal code; read-only on the applicant
    -- profile surface (rendered with the "administrado" badge).
    [CodigoPersonal]       NVARCHAR(40)   NULL,

    CONSTRAINT [PK_AspNetUsers] PRIMARY KEY CLUSTERED ([Id])
);
GO

CREATE UNIQUE NONCLUSTERED INDEX [UserNameIndex]
    ON [dbo].[AspNetUsers] ([NormalizedUserName])
    WHERE [NormalizedUserName] IS NOT NULL;
GO

CREATE NONCLUSTERED INDEX [EmailIndex]
    ON [dbo].[AspNetUsers] ([NormalizedEmail]);
GO

CREATE NONCLUSTERED INDEX [IX_AspNetUsers_Sentinel]
    ON [dbo].[AspNetUsers] ([Id])
    WHERE [IsSystemSentinel] = 1;
GO
