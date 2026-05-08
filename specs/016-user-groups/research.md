# Phase 0 Research: Group-Scoped Reviewer Access

This document resolves every `NEEDS CLARIFICATION` and records the technology
choices behind the Phase 1 design. Each entry uses Decision / Rationale /
Alternatives.

---

## R-001 — Audit mechanism for NFR-005

**Decision**: Add a minimal `dbo.AdminAuditEvents` table and a single
application-side writer (`IAdminAuditWriter`). One row is written for each of:
group create, group rename, group delete, user-membership change. Schema:
`Id BIGINT IDENTITY`, `OccurredAt DATETIMEOFFSET`, `ActorUserId NVARCHAR(450)`,
`Action NVARCHAR(64)`, `TargetType NVARCHAR(64)`, `TargetId NVARCHAR(64)`,
`PayloadJson NVARCHAR(MAX) NULL`. No retention policy, no purge job — out of
scope for this spec.

**Rationale**: NFR-005 explicitly requires admin id + timestamp recording; the
spec authorizes adding a minimal mechanism if none exists. A small dedicated
table keeps audit concerns out of projection services and the signing flow,
gives a clean seam to swap to a structured-logging sink later, and adds zero
managed dependencies (the constitution discourages new NuGet packages).

**Alternatives rejected**:

- **Serilog → SQL sink** — adds a managed dependency and contradicts the
  "reuse what is vendored" posture; emits free-form log rows that are awkward
  to query for "every change to user X's groups".
- **Reuse the signing audit constants and tables** — those are coupled to the
  signing-flow domain (signed file id, signature event, etc.); retrofitting
  them to carry admin/group events would muddle both feature areas.
- **Skip persistence and rely on `Microsoft.Extensions.Logging` to console**
  — logs do not satisfy "recorded with the acting administrator and a
  timestamp" once Aspire-managed dev logs roll off.

---

## R-002 — Demo seed group catalog

**Decision**: The post-deploy script seeds three groups named `Norte`, `Sur`,
`Centro`. No demo memberships are inserted by the seed script.

**Rationale**: Matches every example in the spec; three groups exercise
single-membership and dual-membership reviewer cases, which Story 3 needs.
Names are short, stable, and culturally appropriate for the es-CR locale. E2E
tests already create their own users via `RegisterUserAsync` and assign them
through the admin UI, so the seed only needs the catalog itself.

**Alternatives rejected**:

- **Configuration-driven seed list** — premature: the catalog will be
  edited live by admins in dev as well as prod-shaped envs, and a config knob
  would just need re-tuning per environment.
- **Seed reviewers + memberships in post-deploy** — collides with the
  ephemeral E2E fixture, which expects a known starting set of users from
  `IdentityConfiguration.SeedUsersAsync`. Pushing reviewer-group seeding into
  the dacpac would leak test concerns into the schema.

---

## R-003 — Case-insensitive uniqueness on `Group.Name`

**Decision**: `Groups.Name` is `NVARCHAR(100) NOT NULL` with the column
collation set to `Latin1_General_CI_AI` (case- and accent-insensitive). A
unique non-clustered index on `Name` enforces uniqueness at the database
level. Application-side validation re-checks via a case-insensitive `Any()`
only to render an inline form error before the round-trip; the index is the
authoritative gate.

**Rationale**: A DB-level unique constraint is the only correct way to handle
two admins submitting the same name concurrently. Collation handles the
case-insensitive comparison without a shadow column or computed column.
`CI_AI` ("accent-insensitive") matches the spec's intent — admins should not
be able to create both `Norte` and `Norté` and confuse reviewers.

**Alternatives rejected**:

- **App-only uniqueness check** — race condition between two admins; the
  optimistic-concurrency posture in the constitution would still permit a
  duplicate row.
- **Computed lower-case shadow column with a unique index on it** — adds
  schema and EF mapping noise for no benefit at this scale; collation is the
  idiomatic SQL Server answer.

---

## R-004 — Pushing the group-overlap predicate into EF queries

**Decision**: Introduce `IReviewerScope` in `Application`, with two members:
`bool IsAdmin` and `IReadOnlyCollection<int> GroupIds`. Each affected
projection/service accepts an `IReviewerScope` and applies the predicate at
the EF query level:

```csharp
if (!scope.IsAdmin)
{
    query = query.Where(a =>
        a.Applicant.User.Memberships.Any(m => scope.GroupIds.Contains(m.GroupId)));
}
```

Detail-page authorization in `ReviewController.Review(int id)` calls a
small `IReviewerAccessGuard.CanReview(application, scope)` that runs the
identical predicate in-process against the loaded entity, so query and
authorization stay in sync.

**Rationale**: One shared predicate shape for the reviewer surfaces (queue,
signing inbox, application detail) and the detail-page authorization check
satisfies NFR-001 (query-level filtering on every listing surface) and
NFR-002 (identical server-side check on the detail page). Admins are
short-circuited cleanly. The predicate is small enough to EF-translate
without `AsEnumerable()` materialization. FR-014's "search" surface is the
queue itself: the queue gains a text-search input that composes with the
group-overlap predicate inside the same projection — no separate search
service exists today, and adding one would be premature given the queue is
the single reviewer-facing listing surface.

**Alternatives rejected**:

- **EF Core global query filters (`HasQueryFilter`)** — they apply globally
  and cannot be turned off per-request without `IgnoreQueryFilters`, which
  is footgun-prone (a single missed call would silently leak admins'
  data scope). The explicit predicate composition is safer.
- **Per-surface custom SQL** — fragments the rule across four call sites,
  making the "Admin sees everything" branch easy to forget when a fifth
  surface is added later.
- **Computed view in the dacpac** — overkill for a two-table join and would
  still need the per-request predicate to know who the reviewer is.

---

## R-005 — Optimistic concurrency on memberships

**Decision**: No `RowVersion` on `UserGroupMemberships`. The conflict point
for "two admins editing the same user's groups" is the user row, which
already carries `IdentityUser.ConcurrencyStamp`. The user-edit form renders
the existing stamp and the user-administration service uses it on update.

**Rationale**: The constitution's quality gate calls for optimistic
concurrency on entities with concurrent edit risk. The user is the
contended entity here; memberships are leaf rows keyed by
`(UserId, GroupId)`. Keeping the stamp on the user keeps the conflict
report focused on the human-meaningful edit boundary ("you and another
admin both edited Alice's profile").

**Alternatives rejected**:

- **Per-membership `RowVersion`** — pointless; nobody edits a single
  membership row in isolation.
- **Compute a hash of the membership set** — synthetic and fragile; the
  user stamp already captures the intent.

---

## Summary

All five `NEEDS CLARIFICATION` open items from `spec.md` are resolved here.
There are no remaining unknowns blocking Phase 1.
