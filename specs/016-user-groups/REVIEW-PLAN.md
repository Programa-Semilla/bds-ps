# Review Guide: Group-Scoped Reviewer Access

**Spec:** [spec.md](spec.md) | **Plan:** [plan.md](plan.md) | **Tasks:** [tasks.md](tasks.md)
**Generated:** 2026-05-08

---

## What This Spec Does

Today, every reviewer in FundingPlatform sees every applicant's submission. This feature
introduces a flat catalog of named groups (think "Norte", "Sur", "Centro") that admins
manage; non-admin users get one or more group memberships when admins create or edit them.
The reviewer queue, signing inbox, applicant/application search, and detail pages all gate
on group overlap with the signed-in reviewer. Admins keep their bird's-eye view.

**In scope:**
- Admin CRUD on a flat `Group` catalog with case/accent-insensitive unique names.
- Multi-select group assignment on the existing admin user create/edit form.
- Group-overlap filtering on four reviewer-facing surfaces, plus detail-page 403.
- Cascade delete of memberships when a group is removed; users left with zero groups stay logged in.
- Minimal new audit table (`AdminAuditEvents`) covering group CRUD + membership changes.

**Out of scope** (explicit, see [spec § Out of Scope](spec.md#out-of-scope)): hierarchical
groups, multi-dimensional tags, applicant self-service, per-application reviewer routing,
group-aware admin reports, data migration, per-group quotas, transfer workflows.

## Bigger Picture

This is the first feature to add a real authorization "scope" beyond role on the reviewer
side. Spec 014 (Azure Blob Storage) and 015 (multi-currency quotes) extended capabilities;
this one *narrows* visibility. The ergonomic decisions made here — request-scoped
`IReviewerScope`, composed EF predicates, an explicit `EXISTS` shape rather than EF Core's
`HasQueryFilter` — set the precedent for any future scoping (per-program, per-cohort,
per-tenant) that the platform might grow into. The `AdminAuditEvent` table is also the
project's first general-purpose admin-action audit; downstream features will likely reuse
the same writer.

The plan explicitly rejects EF Core's global query filter ([research.md R-004](research.md#r-004--pushing-the-group-overlap-predicate-into-ef-queries))
because `IgnoreQueryFilters` is footgun-prone: a single missed call would silently leak
admin scope. That decision is worth a careful read — it's the kind of thing that biases
every future scoping feature that lands on top of this one.

---

## Spec Review Guide (30 minutes)

> This guide focuses your 30 minutes on the parts of the spec and plan that need human
> judgment most. Each section points to specific locations and frames the review as
> questions.

### Understanding the approach (8 min)

Read [plan.md § Summary](plan.md#summary) and
[research.md R-004](research.md#r-004--pushing-the-group-overlap-predicate-into-ef-queries)
together. The core architectural move is a small `IReviewerScope` value supplier
([T041](tasks.md#phase-5-user-story-3--reviewer-sees-only-applicants-from-shared-groups-priority-p1))
that every reviewer-facing projection composes into its `IQueryable`. Then read
[data-model.md § ApplicationUser](data-model.md#applicationuser-modifications) to see
how memberships hang off the existing identity row.

- Is "groups attached to `ApplicationUser` directly, with the `Applicant` deriving its
  groups via the `UserId` link" the right abstraction, or should the `Applicant` entity
  carry its own group set? The spec ([Assumptions](spec.md#assumptions)) commits to the
  former; would the latter be more honest about applicant intent?
- The `IReviewerScope` is request-scoped and reads memberships fresh per request
  ([T042](tasks.md#phase-2-foundational-blocking-prerequisites)). Is reading on every
  request acceptable for the queue's hot path, or should this be cached per-request and
  invalidated on membership writes?
- The plan adds `IReviewerScope` to `Application` ([plan.md § Project Structure](plan.md#project-structure))
  and its implementation to `Infrastructure.Identity`. Is reading the DB inside the
  scope provider the right placement, or would a claims-based approach
  (encode group ids into the auth cookie) better satisfy NFR-003 without the per-request
  query?

### Key decisions that need your eyes (12 min)

**Audit storage: a new dedicated table** ([research.md R-001](research.md#r-001--audit-mechanism-for-nfr-005))

A new `dbo.AdminAuditEvents` table with an `IAdminAuditWriter` is added. Alternatives —
Serilog → SQL sink, reusing the signing audit, or relying on `Microsoft.Extensions.Logging`
— were rejected.
- Question: this is now the *project's* admin-audit pattern. Is the schema rich enough
  for future admin actions (user role flip, reset password, etc.), or is it too narrow
  ([data-model.md § AdminAuditEvent](data-model.md#adminauditevent))? Should `Action`
  be an enum table rather than free-form `NVARCHAR(64)`?
- Question: the writer is invoked from service classes ([T021](tasks.md#implementation-for-user-story-1),
  [T033](tasks.md#implementation-for-user-story-2)). Should it be invoked from
  controllers instead, so that the audit trail captures HTTP context (correlation id,
  IP) we don't have at the service layer?

**EF predicate composition vs. global query filter** ([research.md R-004](research.md#r-004--pushing-the-group-overlap-predicate-into-ef-queries))

The plan rejects `HasQueryFilter` because it is global and `IgnoreQueryFilters` is
footgun-prone. Instead, every projection accepts an `IReviewerScope` and composes the
predicate explicitly. Detail-page authorization runs the *same* predicate in-process
against the loaded entity ([research.md § R-004](research.md#r-004--pushing-the-group-overlap-predicate-into-ef-queries)).
- Question: this means four service classes ([T043](tasks.md#implementation-for-user-story-3),
  [T044](tasks.md#implementation-for-user-story-3), [T045](tasks.md#implementation-for-user-story-3),
  [T046](tasks.md#implementation-for-user-story-3)) each compose their own copy of the
  same `Where(...)` clause. Is that duplication acceptable, or should there be a single
  extension method (`IQueryable<Application>.ApplyReviewerScope(IReviewerScope)`) that
  every surface calls? The plan doesn't propose one.
- Question: when a fifth reviewer-facing surface lands later, what stops the engineer
  from forgetting the filter? The plan rejects global filters; what's the affordance
  that catches this in code review?

**Concurrency: rely on `IdentityUser.ConcurrencyStamp`** ([research.md R-005](research.md#r-005--optimistic-concurrency-on-memberships))

The membership rows have no `RowVersion`. Two admins editing the same user's groups
collide on the user-row stamp.
- Question: is the user row the right conflict point? An admin renaming a group while
  another admin assigns that group to a user — does either side detect a conflict, or
  do both succeed silently?
- Question: the [REVIEW-SPEC.md](REVIEW-SPEC.md) flagged EC-007 ("last write wins")
  against the constitution's optimistic-concurrency Quality Gate. Has the plan honored
  that note ([plan.md § Constitution Check](plan.md#constitution-check) — yes, via the
  user stamp)? Is the test in [T030](tasks.md#tests-for-user-story-2)
  ("concurrency-stamp mismatch → ConcurrencyConflict surfaced") sufficient evidence?

**Demoting Admin → Reviewer / promoting Reviewer → Admin**
([spec FR-009](spec.md#user-group-assignment-admin-user-crud), [contracts/admin-users-form.md](contracts/admin-users-form.md))

Promoting a Reviewer to Admin discards their group memberships **silently** and
irrecoverably. Demoting back later requires re-assignment.
- Question: should this discard be surfaced more prominently in the UI (a confirm
  dialog listing the groups about to be detached), or is the silent transition the
  right behavior because admins-with-groups is an invariant violation?
- Question: there's no audit row for the *promotion* itself, only for the
  membership-update side-effect ([contracts/admin-users-form.md § POST /Admin/Users](contracts/admin-users-form.md#post-adminusersid-edit-1)).
  Is that enough trail to reconstruct "who promoted Alice and at what time"?

**Single membership table without role check**
([data-model.md § UserGroupMembership](data-model.md#usergroupmembership))

The DB does *not* enforce "Admin cannot have memberships". The Web/Service layer enforces
it; demoting an admin can re-attach memberships in one transaction.
- Question: is "the table is role-blind on purpose" the right call, or does this set up
  a quiet invariant violation in some future migration where a developer forgets the
  Web-layer check?

### Areas where I'm less certain (5 min)

- [plan.md § Project Structure](plan.md#project-structure) lists `ApplicantSearchService.cs`
  with a "MODIFIED or NEW" qualifier and [T045](tasks.md#implementation-for-user-story-3)
  says "locate via `SearchController` or the corresponding projection class". I could not
  verify from the plan alone which existing surface FR-014 actually maps to. The
  [REVIEW-SPEC.md § Optional](REVIEW-SPEC.md#recommendations) raised the same concern; the
  plan defers it. If no reviewer-facing search exists yet, T045 is a forward-looking
  scaffold rather than a real modification — and that should be explicit before
  implementation starts.
- [research.md R-003](research.md#r-003--case-insensitive-uniqueness-on-groupname) selects
  `Latin1_General_CI_AI` collation. Accent-insensitive matters for `Norte` vs. `Norté`,
  but it also means `Pacífico` and `Pacifico` collide. That seems intentional but the
  spec doesn't explicitly require *accent*-insensitivity (only case-insensitivity in
  [FR-001](spec.md#functional-requirements)). Is the stricter rule what admins want?
- NFR-003 ("changes take effect on the next request without sign-out") is implemented via
  request-scoped DI ([T042](tasks.md#phase-2-foundational-blocking-prerequisites)) and
  exercised manually in [quickstart.md step 5–6](quickstart.md#steps), but no dedicated
  test task verifies it. The behavior is implicit in [T040](tasks.md#tests-for-user-story-3)
  if the integration test changes memberships mid-suite, but not explicitly. I'd want
  one focused test that asserts membership change → next request reflects it.

### Risks and open questions (5 min)

- [Spec FR-002](spec.md#functional-requirements) says "non-admin → 403" on group
  endpoints. Is anonymous access also 403, or does the existing `[Authorize]` chain
  short-circuit to 401/redirect-to-login? The contract ([admin-groups.md](contracts/admin-groups.md))
  says "authenticated but not in role Admin" gets 403; what happens for *unauthenticated*
  callers? Should the spec call this out for completeness?
- [Spec EC "in-flight detail page requests for out-of-scope applicants are denied"](spec.md#edge-cases):
  if a reviewer is mid-edit on a detail page when their group is removed, the next
  *POST* will 403. Will the form show a graceful error, or will the reviewer just see a
  raw 403 page? The plan does not prescribe a UX for this case.
- [data-model.md § Demo seed](data-model.md#demo-seed-post-deploy) inserts three groups
  unconditionally; [T004](tasks.md#schema-dacpac) says "if absent (idempotent guard)".
  The data-model doc and the task do not match exactly. Does the seed run on every
  deploy (including prod) or only first-time?
- The [E2E delivery bar in CLAUDE.md](../../CLAUDE.md) says no feature ships until the
  full E2E suite is personally executed and green ([T054](tasks.md#phase-7-polish--cross-cutting-concerns)).
  Four new E2E test classes ([T029](tasks.md#implementation-for-user-story-1),
  [T038](tasks.md#implementation-for-user-story-2), [T048](tasks.md#implementation-for-user-story-3),
  [T050](tasks.md#tests-for-user-story-4)) plus existing suite — is the AspireFixture's
  ephemeral SQL container fast enough that this gate doesn't become a delivery
  bottleneck?

## Prior Review Feedback

The feature has a [REVIEW-SPEC.md](REVIEW-SPEC.md) from the spec-review pass. The plan
addressed those notes as follows:

| # | Reviewer | Original Concern | How Addressed | Spec/Plan Location |
|---|----------|-----------------|---------------|---------------|
| 1 | Claude (review-spec) | EC-007 concurrency policy may deviate from constitution Quality Gate | Plan adopts the user-row `ConcurrencyStamp` as the conflict point; [research R-005](research.md#r-005--optimistic-concurrency-on-memberships) records the decision | [spec § Edge Cases](spec.md#edge-cases) |
| 2 | Claude (review-spec) | FR-014 ambiguity: which existing search surface? | Deferred to implementation; [T045](tasks.md#implementation-for-user-story-3) carries the "locate via SearchController or corresponding projection class" qualifier — **still ambiguous in plan** | [plan.md § Project Structure](plan.md#project-structure) |
| 3 | Claude (review-spec) | NFR-005 audit mechanism deferral | Resolved: new `AdminAuditEvent` table; [research R-001](research.md#r-001--audit-mechanism-for-nfr-005) | [data-model.md § AdminAuditEvent](data-model.md#adminauditevent) |
| 4 | Claude (review-spec) | Consider lifting "≥1 group" invariant into the domain | Plan keeps validation at Web/Service boundary; rationale: the rule depends on role which lives in Identity, so a domain method would have to take role as a parameter and would not be much cleaner. **Constitution II "Rich Domain Model" — accepted with judgment, not encoded as a complexity-tracking entry.** | [plan.md § Constitution Check](plan.md#constitution-check) |

---
*Full context in linked [spec](spec.md) and [plan](plan.md).*
