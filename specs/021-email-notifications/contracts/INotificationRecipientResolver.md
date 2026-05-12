# Contract: `INotificationRecipientResolver`

**Layer**: `FundingPlatform.Application` (interface) / `FundingPlatform.Infrastructure` (impl)
**Spec FRs**: FR-006, FR-007, FR-008, FR-009, FR-010, FR-011, FR-012, FR-013.

## Interface

```csharp
namespace FundingPlatform.Application.Notifications;

public interface INotificationRecipientResolver
{
    Task<IReadOnlyList<NotificationRecipient>> ResolveAsync(
        NotificationOutbox row,
        CancellationToken ct);
}
```

## Behavior contract

1. **Per-event resolution rules** (matches §Recipient Rules table in spec):

   | EventType | Bucket: `Applicant` | Bucket: `Reviewer` | Bucket: `Admin` |
   |---|---|---|---|
   | `APPLICATION_SUBMITTED_APPLICANT` | applicant user | — | — |
   | `APPLICATION_SUBMITTED_REVIEWER` | — | reviewers of the application's current stage group | participating admins |
   | `RETURNED_TO_APPLICANT` | applicant user | — | participating admins |
   | `RESUBMITTED_BY_APPLICANT` | — | reviewers of the current stage group | participating admins |
   | `APPLICATION_APPROVED` | applicant user | — | participating admins |
   | `APPLICATION_REJECTED` | applicant user | — | participating admins |

2. **Resolution is done at dispatch time, not at outbox-write time** (EC-002, EC-003, EC-004):
   - Email address read from the user's current ASP.NET Identity record.
   - Stage group queried at dispatch time (spec 016 read path).
   - Participating-admin predicate evaluated at dispatch time.

3. **Participating-admin predicate** (per R-006 in `research.md`):
   - Source: `DISTINCT vh.UserId FROM dbo.VersionHistory vh WHERE vh.ApplicationId = @id AND vh.UserId IS NOT NULL`
   - Filter: `user IS CURRENTLY in role "Admin"` (via `UserManager.IsInRoleAsync` or the equivalent EF-composed predicate on `AspNetUserRoles`).
   - Known limitation: a user who acted as admin in the past and is now demoted to reviewer will NOT match the predicate. **Spec EC-002 is therefore only partially supported in v1**; the gap is tracked under **OQ-011** (new open question — deferred to a future spec).

4. **Dedup** (FR-012):
   - After collecting all candidate `NotificationRecipient`s for the outbox row, group by `UserId` (or by email when `UserId` is null).
   - Keep one entry per group. The chosen entry is the one whose `Bucket` has the lowest ordinal under priority `Applicant < Reviewer < Admin`.
   - The `TemplateVariantKey` carried on the kept entry MUST correspond to the kept bucket (so the renderer picks the right variant).

5. **Recipient email validation** (FR-029):
   - If the resolved user's email is null or whitespace, the recipient is still returned in the list with `Email=""` and the worker MUST skip with `Status=Skipped` + `LastError="MissingEmail"`. Other recipients on the same outbox row MUST still be processed.

6. **No mutation** — the resolver MUST NOT write to the database. It is a pure read over `Applications`, `AspNetUsers`, `AspNetUserRoles`, `Groups`, `UserGroupMemberships`, `VersionHistory`. Cancellation is honored.

## EF query composition (impl notes for `NotificationRecipientResolver`)

```csharp
// Stage-group reviewers
var reviewers =
    from a in _context.Applications
    where a.Id == row.ApplicationId
    from sg in a.CurrentStage!.AssignedGroups
    from m in _context.UserGroupMemberships
    where m.GroupId == sg.GroupId
    select new { m.UserId, m.User!.Email, m.User!.UserName /* DisplayName via projection */ };

// Participating admins (v1 predicate)
var admins =
    from vh in _context.VersionHistories
    where vh.ApplicationId == row.ApplicationId && vh.UserId != null
    join uRole in _context.UserRoles on vh.UserId equals uRole.UserId
    join role in _context.Roles on uRole.RoleId equals role.Id
    where role.NormalizedName == "ADMIN"
    select new { vh.UserId, vh.User!.Email, vh.User!.UserName };
```

(Pseudo-EF; the implementation will use the actual navigation property names from `FundingPlatformDbContext`.)

## Test surface

| Test | Layer | Asserts |
|---|---|---|
| `NotificationRecipientResolverTests` | Integration | Each event type yields the expected per-bucket recipients against a seeded DB (SC-002 matrix). |
| `ParticipatingAdminPredicateTests` | Integration | Currently-admin actor who acted IS in the bucket. Currently-reviewer actor who acted (demoted admin) is NOT. Currently-admin actor who never acted IS NOT. Pure-admin role with no `VersionHistory` row IS NOT. |
| `DedupBucketPriorityTests` | Integration | A user qualifying as both applicant + admin gets exactly one recipient row with `Bucket=Applicant` and the applicant-variant template key (US8 acceptance scenario 3). |
| `MissingEmailSkipTests` | Integration | An applicant with `Email=NULL` produces a recipient row that the worker turns into `Status=Skipped`; reviewers on the same row still process normally. |
