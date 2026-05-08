# Phase 1 Data Model: Group-Scoped Reviewer Access

## Entity overview

| Entity | Purpose | Lifetime | Owner |
|---|---|---|---|
| `Group` | Named partition used to scope reviewer access. | Persistent. | `FundingPlatform.Domain` |
| `UserGroupMembership` | Many-to-many join between `ApplicationUser` and `Group`. | Persistent. Cascades on either side delete. | `FundingPlatform.Domain` |
| `AdminAuditEvent` | Append-only record of admin actions (group CRUD + membership change). | Persistent. No cascade, no purge in this spec. | `FundingPlatform.Domain` |
| `ApplicationUser` (existing) | Identity row. Gains a navigation collection of `UserGroupMembership`. | Persistent. Unchanged column shape; navigation only. | `FundingPlatform.Domain` |

## Group

### Fields

| Name | Type | Constraint | Notes |
|---|---|---|---|
| `Id` | `int` | PK, IDENTITY | Surrogate key. Kept small; group count is bounded. |
| `Name` | `NVARCHAR(100) COLLATE Latin1_General_CI_AI` | NOT NULL, UNIQUE | Case- and accent-insensitive unique. |
| `CreatedAt` | `DATETIMEOFFSET` | NOT NULL, default `SYSUTCDATETIME()` | Set on insert. |
| `UpdatedAt` | `DATETIMEOFFSET` | NOT NULL | Bumped by `Rename`. |

### Domain methods

- `static Group Create(string name)` — trims, validates non-empty/length ≤ 100, returns a new instance with `CreatedAt = UpdatedAt = DateTimeOffset.UtcNow`.
- `void Rename(string newName)` — same validation; sets `UpdatedAt`. Idempotent if the trimmed new name equals the current name.

### Validation rules

- Trimmed name MUST be non-empty.
- Trimmed name length MUST be 1–100.
- Uniqueness across all groups (case/accent-insensitive) — enforced by the SQL Server unique index; the entity does not check this.

### Relationships

- One-to-many with `UserGroupMembership` via `Memberships` (private backing collection, `IReadOnlyCollection<UserGroupMembership> Memberships`).

### Cascade behavior

- Deleting a `Group` cascades and removes all `UserGroupMembership` rows that reference it (FR-004).

## UserGroupMembership

### Fields

| Name | Type | Constraint | Notes |
|---|---|---|---|
| `UserId` | `NVARCHAR(450)` | PK part 1, FK → `dbo.AspNetUsers(Id)` | Matches the existing Identity user PK width. |
| `GroupId` | `int` | PK part 2, FK → `dbo.Groups(Id)` | |
| `AssignedAt` | `DATETIMEOFFSET` | NOT NULL | When the membership was created. Useful for the audit story even though the canonical record is in `AdminAuditEvents`. |

### Indexes

- Composite primary key on `(UserId, GroupId)`.
- Non-clustered index on `(GroupId, UserId)` to support the reviewer-side scope predicate without scanning the table.

### Relationships

- `User` → `ApplicationUser` (required, cascade on user delete).
- `Group` → `Group` (required, cascade on group delete — see FR-004).

### Validation rules

- Composite-key uniqueness is enforced by the PK; no entity-level check.
- The Web layer prevents inserting Admin-role memberships (FR-009); the table itself is role-blind on purpose, so demoting an admin to reviewer can re-attach memberships in a single transaction.

## AdminAuditEvent

### Fields

| Name | Type | Constraint | Notes |
|---|---|---|---|
| `Id` | `BIGINT` | PK, IDENTITY | |
| `OccurredAt` | `DATETIMEOFFSET` | NOT NULL | Default `SYSUTCDATETIME()`. |
| `ActorUserId` | `NVARCHAR(450)` | NOT NULL, FK → `dbo.AspNetUsers(Id)` (no cascade) | The admin who acted. |
| `Action` | `NVARCHAR(64)` | NOT NULL | One of: `group.create`, `group.rename`, `group.delete`, `user.memberships.update`. |
| `TargetType` | `NVARCHAR(64)` | NOT NULL | `group` or `user`. |
| `TargetId` | `NVARCHAR(64)` | NOT NULL | Group id (as text) or user id. |
| `PayloadJson` | `NVARCHAR(MAX)` | NULL | Optional structured detail (e.g., `{"old":"Norte","new":"Norte Pacífico"}` for rename, `{"added":[2,3],"removed":[1]}` for membership update). |

### Domain methods

- `static AdminAuditEvent Record(string actorUserId, string action, string targetType, string targetId, string? payloadJson)` — single factory, validates non-empty fields.

### Relationships

- `Actor` → `ApplicationUser` (required, no cascade — audit rows survive user deletion).

## ApplicationUser (modifications)

No new columns. Add a navigation:

```csharp
public virtual ICollection<UserGroupMembership> Memberships { get; private set; } = new List<UserGroupMembership>();
```

EF configuration in `ApplicationUserConfiguration` (new) maps the navigation. The existing `IdentityUser` schema (and its `ConcurrencyStamp`) is unchanged.

## Invariants

- **Admin invariant** (FR-009): an `ApplicationUser` whose role set contains `Admin` MUST have zero rows in `UserGroupMemberships`. Enforced at the Web/Service boundary; the DB does not encode this because `AspNetUserRoles` is the role join and we do not want a cross-table check constraint.
- **Reviewer scoping invariant** (FR-011 ‒ FR-014): the EF predicate `applicant.User.Memberships.Any(m => scope.GroupIds.Contains(m.GroupId))` is the canonical filter. Any new reviewer-facing surface MUST compose it before serving rows.

## State transitions

None beyond create/update/delete. The application has no lifecycle states for groups or memberships.

## Demo seed (post-deploy)

Three rows in `dbo.Groups`:

```sql
INSERT INTO dbo.Groups (Name, CreatedAt, UpdatedAt)
VALUES (N'Norte', SYSUTCDATETIME(), SYSUTCDATETIME()),
       (N'Sur',   SYSUTCDATETIME(), SYSUTCDATETIME()),
       (N'Centro',SYSUTCDATETIME(), SYSUTCDATETIME());
```

No demo memberships are inserted by the post-deploy script; tests and real
admins assign users through the UI.
