# Implementation Plan: Group-Scoped Reviewer Access

**Branch**: `feature/group-users` | **Date**: 2026-05-07 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `specs/016-user-groups/spec.md`

## Summary

Introduce a `Group` catalog and a many-to-many `User ↔ Group` membership. Admins manage the catalog, assign one or more groups when creating/editing non-admin users, and the system filters every reviewer-facing list (queue, signing inbox, applicant/application search) at the EF query level so a reviewer sees only applicants whose `ApplicationUser` shares at least one group. Detail-page authorization enforces the same overlap server-side. Admins bypass the filter; applicants always see their own application.

Technical approach: a `Group` aggregate plus a `UserGroupMembership` join entity (its own EF entity, not a skip-nav collection on `ApplicationUser`, because the project's optimistic-concurrency and audit conventions live on entity rows, and we want the same on memberships). Reviewer surfaces gain a small `IReviewerScope` value supplier that the existing projection services compose into their EF queries. Schema lives in the dacpac. Audit goes to a new minimal `AdminAuditEvent` table (no project-wide mechanism exists yet — FR-005 explicitly authorizes adding one).

## Technical Context

**Language/Version**: C# 13 / .NET 10.0
**Primary Dependencies**: ASP.NET MVC, EF Core 10, ASP.NET Identity, .NET Aspire, Tabler.io (vendored), Playwright for .NET, Syncfusion HtmlToPdfConverter (already in solution; not used by this feature)
**Storage**: SQL Server via the `FundingPlatform.Database` dacpac. New tables `dbo.Groups`, `dbo.UserGroupMemberships`, `dbo.AdminAuditEvents`. Post-deploy seed adds the demo group catalog.
**Testing**: xUnit + Playwright. E2E suite (`tests/FundingPlatform.Tests.E2E`) is the primary quality gate, run against the Aspire-orchestrated stack with ephemeral SQL.
**Target Platform**: Linux server (.NET on Linux). Browser-side Playwright runs against the Aspire Web project.
**Project Type**: Server-side rendered ASP.NET MVC web app (single project tree under `src/`).
**Performance Goals**: Reviewer queue / signing inbox / search remain p95 < 500 ms with the group-overlap predicate added; no measurable regression vs. today's queries (each gains an `EXISTS` against `UserGroupMemberships` joined on `Applicant.UserId`).
**Constraints**:
- NFR-001 — group filter applied at the EF query level on every listing surface (no in-memory post-filtering).
- NFR-002 — detail-page authorization server-side; URL tampering must not bypass.
- NFR-003 — membership changes take effect on the next request without forcing sign-out.
- NFR-004 — all new copy localized in es-CR via the existing static resource-class pattern.
- NFR-005 — group create/rename/delete and user-membership changes recorded with admin id + timestamp.
**Scale/Scope**: Internal admin tool. Group count ≤ low tens. Users ≤ low hundreds for the foreseeable future. Surfaces touched: 1 new admin-area module (Groups CRUD), 1 modified admin form (Users), 4 reviewer-facing list/detail surfaces.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Verdict | Notes |
|---|---|---|
| I. Clean Architecture | PASS | `Group`, `UserGroupMembership`, `AdminAuditEvent` live in `Domain`. Service interfaces (`IGroupService`, `IUserGroupAssignmentService`, `IAdminAuditWriter`) in `Application`. EF configurations in `Infrastructure`. Controllers/views in `Web`. Reviewer surfaces compose a new `IReviewerScope` from `Application`; the EF predicate translation happens inside Infrastructure-side projection services. |
| II. Rich Domain Model | PASS | `Group` exposes `Rename(string newName)`, validates non-empty/length. Membership lifecycle goes through `ApplicationUserGroupBinder` domain methods (`SetMemberships(IEnumerable<int> groupIds)`, `ClearMemberships()`) on a small domain helper rather than scattering logic in the service. Validation lives in the entity. |
| III. End-to-End Testing | PASS | Each of the four user stories gets its own Playwright test class with golden path + key error scenarios. Existing `AspireFixture` and Page Object Model conventions are reused. New `AdminGroupsPage` and an extension to `AdminUsersPage` for the multi-select. |
| IV. Schema-First DB | PASS | All new tables and the FK from `UserGroupMemberships → AspNetUsers` are added to the dacpac. Demo group seed via post-deploy script. EF Core uses these tables; no migrations, no `EnsureCreated`. |
| V. Specification-Driven | PASS | Spec already accepted (`spec.md` + `REVIEW-SPEC.md`). This plan + `tasks.md` are the next two artifacts. Implementation follows. |
| VI. Simplicity / YAGNI | PASS | Single membership table, no hierarchy, no per-application reviewer assignment, no quotas, no transfer workflows — all explicitly deferred in spec's Out of Scope. The audit table is the minimum to satisfy NFR-005. |

No complexity violations to track.

## Project Structure

### Documentation (this feature)

```text
specs/016-user-groups/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output (HTTP route contracts)
│   ├── admin-groups.md
│   └── admin-users-form.md
├── spec.md              # Existing
├── REVIEW-SPEC.md       # Existing
└── tasks.md             # Phase 2 output (created by /speckit-tasks)
```

### Source Code (repository root)

```text
src/
├── FundingPlatform.Domain/
│   └── Entities/
│       ├── Group.cs                              # NEW — aggregate root
│       ├── UserGroupMembership.cs                # NEW — join entity (UserId, GroupId, AssignedAt)
│       ├── AdminAuditEvent.cs                    # NEW — minimal audit row
│       └── ApplicationUser.cs                    # MODIFIED — add Groups navigation (IReadOnlyCollection)
├── FundingPlatform.Application/
│   ├── Admin/
│   │   ├── Groups/
│   │   │   ├── IGroupService.cs                  # NEW — Create/Rename/Delete/List
│   │   │   └── GroupCommands.cs                  # NEW — DTOs
│   │   └── Users/
│   │       └── IUserAdministrationService.cs     # MODIFIED — Create/Edit accept group ids
│   ├── Audit/
│   │   └── IAdminAuditWriter.cs                  # NEW
│   └── Reviewer/
│       └── IReviewerScope.cs                     # NEW — exposes ReviewerGroupIds + IsAdmin
├── FundingPlatform.Infrastructure/
│   ├── Persistence/
│   │   ├── AppDbContext.cs                       # MODIFIED — DbSet<Group>, DbSet<UserGroupMembership>, DbSet<AdminAuditEvent>
│   │   └── Configurations/
│   │       ├── GroupConfiguration.cs             # NEW — unique name (case-insensitive collation)
│   │       ├── UserGroupMembershipConfiguration.cs # NEW — composite PK, cascade on Group delete
│   │       └── AdminAuditEventConfiguration.cs   # NEW
│   ├── Identity/
│   │   ├── UserAdministrationService.cs          # MODIFIED — accept group ids, enforce role rules, write audit
│   │   └── ReviewerScopeProvider.cs              # NEW — implements IReviewerScope from claims/principal
│   ├── Audit/
│   │   └── AdminAuditWriter.cs                   # NEW
│   └── Services/                                 # MODIFIED — push group-overlap predicate into:
│       ├── ReviewerQueueProjection.cs            # MODIFIED (FR-011)
│       ├── SignedUploadService.cs                # MODIFIED (FR-013)
│       ├── ApplicantSearchService.cs             # MODIFIED or NEW (FR-014)
│       └── GroupService.cs                       # NEW — implements IGroupService
├── FundingPlatform.Web/
│   ├── Controllers/Admin/
│   │   ├── AdminGroupsController.cs              # NEW — Index, Create, Edit (rename), Delete
│   │   └── AdminUsersController.cs               # MODIFIED — bind GroupIds[]; render multi-select
│   ├── Controllers/
│   │   └── ReviewController.cs                   # MODIFIED — pass IReviewerScope to projection; deny detail when no overlap
│   ├── Views/Admin/Groups/
│   │   ├── Index.cshtml
│   │   ├── Create.cshtml
│   │   └── Edit.cshtml                           # NEW
│   ├── ViewModels/Admin/
│   │   ├── AdminGroupsIndexViewModel.cs          # NEW
│   │   ├── AdminGroupCreateViewModel.cs          # NEW
│   │   ├── AdminGroupEditViewModel.cs            # NEW
│   │   ├── AdminUserCreateViewModel.cs           # MODIFIED — int[] GroupIds + AvailableGroups
│   │   └── AdminUserEditViewModel.cs             # MODIFIED — same fields + RowVersion (existing)
│   └── Resources/
│       └── AdminGroupsResources.cs               # NEW — es-CR copy
└── FundingPlatform.Database/
    ├── Tables/
    │   ├── dbo.Groups.sql                        # NEW
    │   ├── dbo.UserGroupMemberships.sql          # NEW
    │   └── dbo.AdminAuditEvents.sql              # NEW
    └── PostDeployment/
        └── SeedData.sql                          # MODIFIED — append demo groups + sample memberships

tests/
├── FundingPlatform.Tests.Unit/
│   ├── Domain/GroupTests.cs                      # NEW — name validation, rename
│   └── Application/ReviewerScopePredicateTests.cs# NEW — query-shape tests
├── FundingPlatform.Tests.Integration/
│   ├── GroupServiceTests.cs                      # NEW — DB-backed
│   ├── UserAdministrationGroupsTests.cs          # NEW — CRUD with groups
│   └── ReviewerQueueScopeTests.cs                # NEW — query-level filter against real DB
└── FundingPlatform.Tests.E2E/
    ├── PageObjects/
    │   ├── AdminGroupsPage.cs                    # NEW
    │   └── AdminUserFormPage.cs                  # NEW or extended
    └── Tests/
        ├── AdminGroupCrudTests.cs                # NEW — Story 1
        ├── AdminUserGroupAssignmentTests.cs      # NEW — Story 2
        ├── ReviewerScopeTests.cs                 # NEW — Story 3 (queue, detail, signing inbox, search)
        └── GroupDeletionCascadeTests.cs          # NEW — Story 4
```

**Structure Decision**: Single-project ASP.NET MVC layout (Option 1 from the template), aligned with the existing `FundingPlatform.*` solution. No new top-level projects. The dacpac is the schema source of truth; EF Core consumes the deployed schema. Reviewer surfaces evolve in place — group-overlap is wired into existing projection/service classes rather than introducing parallel "scoped" variants, to keep one execution path per surface.

## Phase 0 — Outline & Research

Open questions identified in `spec.md`:

1. **NEEDS CLARIFICATION (resolved here): Audit mechanism for NFR-005.** No project-wide audit log exists; entities use ad-hoc `CreatedByUserId` columns and the signing flow uses static action constants. Decision: add a minimal `AdminAuditEvent` table (Id, OccurredAt, ActorUserId, Action, TargetType, TargetId, Payload-JSON), written via `IAdminAuditWriter` whenever a group is created/renamed/deleted or a user's memberships change. Rationale: smallest viable shape that satisfies NFR-005, avoids leaking audit concerns into projection services, and leaves a clear seam to swap to a structured-logging sink later. Alternatives rejected: (a) Serilog-to-table sink — adds a managed dependency, contradicts "reuse what is vendored"; (b) reuse signing audit — its semantics are coupled to the signing flow, retrofitting would muddle both.

2. **NEEDS CLARIFICATION (resolved here): Demo seed group names.** Decision: ship `Norte`, `Sur`, `Centro` as the post-deploy seed (the spec's working assumption). Rationale: matches every example in the spec, three groups exercise both single- and multi-membership paths, names are short and stable for E2E. Alternatives rejected: parameterized seed via configuration (premature) and seeding reviewers into specific groups in the post-deploy script (E2E creates its own users via the existing `RegisterUserAsync` helper, so default seed only needs the catalog).

3. **Best practice: case-insensitive uniqueness for group name.** SQL Server collation `Latin1_General_CI_AI` on the `Name` column plus a unique index. EF Core trusts the index; the application-side check uses `ToLower()` only for the inline form-validation message. Alternatives rejected: app-only uniqueness (race condition between two concurrent admins) and computed lower-case shadow column (adds maintenance for no benefit at this scale).

4. **Best practice: pushing the group-overlap predicate into EF queries.** Each affected projection accepts an `IReviewerScope` and adds `where applicant.User.Memberships.Any(m => scope.GroupIds.Contains(m.GroupId))` to its `IQueryable<Application>`. Admins receive an `IReviewerScope` whose `IsAdmin == true`, and the predicate is short-circuited (`if (scope.IsAdmin) return query;`). This gives one shared predicate-shape across surfaces and keeps detail-page auth identical. Alternative rejected: query filters via `HasQueryFilter` — they apply globally and cannot be turned off per-request without `IgnoreQueryFilters`, which would risk an accidental admin-bypass mistake.

5. **Best practice: `RowVersion` on memberships?** The spec relies on the existing optimistic-concurrency token on `ApplicationUser` (`ConcurrencyStamp` from `IdentityUser`) to handle concurrent edits of the same user's memberships (edge case). Memberships themselves do not need their own `RowVersion`; they are leaf records keyed by `(UserId, GroupId)` and the user-row stamp is the conflict point.

**Output**: `research.md` consolidates the five items above as Decision/Rationale/Alternatives entries. All `NEEDS CLARIFICATION` markers from the spec's Open Questions are resolved.

## Phase 1 — Design & Contracts

**Prerequisites**: `research.md` complete (Phase 0 above).

1. **`data-model.md`** captures `Group`, `UserGroupMembership`, `AdminAuditEvent`, and the modifications to `ApplicationUser`. Includes:
   - Field list, types, and constraints.
   - Relationships and cascade rules (`Group.Delete` cascades through `UserGroupMembership`; `ApplicationUser.Delete` cascades the same way; `AdminAuditEvent` has no cascade — soft retention).
   - Validation rules (`Group.Name` non-empty, ≤ 100 chars, case-insensitively unique).
   - State transitions: none beyond CRUD; the rich-domain methods are documented.

2. **`contracts/`** documents two HTTP-shaped surfaces:
   - `admin-groups.md` — routes (`GET /Admin/Groups`, `POST /Admin/Groups`, `POST /Admin/Groups/{id}/Edit`, `POST /Admin/Groups/{id}/Delete`), bound view models, validation responses, and authorization (Admin only, 403 otherwise).
   - `admin-users-form.md` — the additions to the existing user create/edit POST payload (`GroupIds[]`), the conditional rendering rule (hidden when `Role == Admin`), the discard-on-promote behavior, and the validation messages.

   No public API or external contract surface is added; everything is internal MVC.

3. **`quickstart.md`** — a one-page "stand the feature up locally" guide: run AppHost, sign in as `admin@FundingPlatform.com / Sentinel123!`, navigate to `/Admin/Groups`, create two groups, edit a seeded reviewer, assign one group, sign in as that reviewer, observe filtered queue.

4. **Agent context update** — replace the plan reference between the `<!-- SPECKIT START -->` / `<!-- SPECKIT END -->` markers in `CLAUDE.md` to point at this plan file.

**Output**: `data-model.md`, `contracts/admin-groups.md`, `contracts/admin-users-form.md`, `quickstart.md`, updated `CLAUDE.md`.

## Constitution Re-check (post-design)

Re-evaluating after the design above:

| Principle | Verdict | Notes |
|---|---|---|
| I. Clean Architecture | PASS | No new cross-layer references introduced. `IReviewerScope` is in `Application`; its implementation in `Infrastructure.Identity` reads only from `ClaimsPrincipal` + DbContext. |
| II. Rich Domain Model | PASS | `Group.Rename`, `Group` constructor enforce invariants; membership mutations go through a domain helper; service layer is a thin orchestrator + audit writer. |
| III. End-to-End Testing | PASS | Four E2E test classes mapped one-to-one to the four user stories. Each has independently-runnable golden path + edge cases listed in the spec. |
| IV. Schema-First DB | PASS | Three new `.sql` files in `Tables/`; one append to `PostDeployment/SeedData.sql`. EF Core configurations match the dacpac shape. No migrations. |
| V. Specification-Driven | PASS | Spec → plan → tasks order maintained. |
| VI. Simplicity / YAGNI | PASS | No premature abstractions. The audit table is intentionally minimal; `IReviewerScope` exists only because it is used by 4 services and by detail-page auth. |

No deviations to track in Complexity Tracking.

## Complexity Tracking

> Empty — no Constitution violations to justify.
