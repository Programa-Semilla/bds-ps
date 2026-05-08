CREATE TABLE [dbo].[UserGroupMemberships]
(
    [UserId]     NVARCHAR(450)   NOT NULL,
    [GroupId]    INT             NOT NULL,
    [AssignedAt] DATETIMEOFFSET  NOT NULL CONSTRAINT [DF_UserGroupMemberships_AssignedAt] DEFAULT (SYSUTCDATETIME()),

    CONSTRAINT [PK_UserGroupMemberships] PRIMARY KEY CLUSTERED ([UserId], [GroupId]),
    CONSTRAINT [FK_UserGroupMemberships_AspNetUsers]
        FOREIGN KEY ([UserId]) REFERENCES [dbo].[AspNetUsers] ([Id]) ON DELETE CASCADE,
    CONSTRAINT [FK_UserGroupMemberships_Groups]
        FOREIGN KEY ([GroupId]) REFERENCES [dbo].[Groups] ([Id]) ON DELETE CASCADE
);
GO

-- Spec 016 / NFR-001 — supports the reviewer-side group-overlap predicate
-- (`exists … where m.GroupId IN (...) AND m.UserId = applicant.UserId`).
-- Composite-key clustered index already covers `(UserId, GroupId)` lookups;
-- this auxiliary index covers the reverse direction (`GroupId → UserId`).
CREATE NONCLUSTERED INDEX [IX_UserGroupMemberships_GroupId_UserId]
    ON [dbo].[UserGroupMemberships] ([GroupId], [UserId]);
GO
