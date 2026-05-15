CREATE TABLE [dbo].[PasswordResetTokens]
(
    [Id]          BIGINT          IDENTITY(1,1) NOT NULL,
    [UserId]      NVARCHAR(450)   NOT NULL,
    [TokenHash]   VARBINARY(64)   NOT NULL,
    [IssuedAt]    DATETIMEOFFSET(0)    NOT NULL CONSTRAINT [DF_PasswordResetTokens_IssuedAt] DEFAULT (SYSUTCDATETIME()),
    [ExpiresAt]   DATETIMEOFFSET(0)    NOT NULL,
    [ConsumedAt]  DATETIMEOFFSET(0)    NULL,

    CONSTRAINT [PK_PasswordResetTokens] PRIMARY KEY CLUSTERED ([Id]),
    -- Spec 021 / FR-028 — raw token never persisted; TokenHash is the SHA-256
    -- digest of the dispatched token, so lookup-on-consume hashes the inbound
    -- token and compares against this column. Cascade on user delete so an
    -- orphaned applicant cleanup also wipes outstanding tokens.
    CONSTRAINT [FK_PasswordResetTokens_AspNetUsers]
        FOREIGN KEY ([UserId]) REFERENCES [dbo].[AspNetUsers] ([Id]) ON DELETE CASCADE
);
GO

CREATE NONCLUSTERED INDEX [IX_PasswordResetTokens_UserId_IssuedAt]
    ON [dbo].[PasswordResetTokens] ([UserId], [IssuedAt] DESC);
GO

-- Covers the consume path: hash-based lookup is the hot read.
CREATE UNIQUE NONCLUSTERED INDEX [UX_PasswordResetTokens_TokenHash]
    ON [dbo].[PasswordResetTokens] ([TokenHash]);
GO
