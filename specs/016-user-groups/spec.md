# Feature Specification: Group-Scoped Reviewer Access

**Feature Branch**: `feature/group-users`
**Created**: 2026-05-07
**Status**: Draft
**Input**: User description: "non admin users must belong to one or more groups, so reviewers can review only applicants from the same groups assigned. Only admins can create groups. Groups is assigned by the time of creation or later when editing an user."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Admin manages the catalog of groups (Priority: P1)

An administrator opens the admin area and creates the named groups that will partition reviewer access (e.g., "Norte", "Sur", "Centro"). Names are unique. Admin can rename or delete a group at any time. Only admins reach these screens.

**Why this priority**: Without a group catalog, no other behavior in this feature has anything to assign. This is the foundation.

**Independent Test**: Sign in as admin, navigate to the groups screen, create two groups with distinct names, attempt to create a third with a duplicate name (rejected), rename one, delete one. Sign in as a non-admin and confirm the screen returns 403.

**Acceptance Scenarios**:

1. **Given** an admin on the groups screen, **When** the admin submits a new group with a unique non-empty name, **Then** the group appears in the list with member count zero.
2. **Given** an admin on the groups screen, **When** the admin submits a name that already exists (case-insensitive match), **Then** a validation error is shown inline and no new group is created.
3. **Given** an admin renaming a group with members, **When** the admin saves, **Then** all existing memberships remain intact under the new name.
4. **Given** a reviewer or applicant signed in, **When** they request a group-management URL directly, **Then** the request is denied with 403.

---

### User Story 2 - Admin assigns one or more groups to non-admin users (Priority: P1)

When an admin creates a new Applicant or Reviewer, the user form requires at least one group. The same constraint applies on edit. The selector is multi-select. When the role is Admin, the group selector is hidden, and any prior memberships are discarded on save.

**Why this priority**: User Story 3 (visibility filtering) cannot start producing correct results until users have group memberships. Story 1 + Story 2 together form the smallest deployable slice that establishes the data shape; visibility filtering can land afterwards in Story 3.

**Independent Test**: As admin, create a Reviewer with zero groups selected (blocked with validation error), then with two groups (succeeds). Edit an existing Applicant: change the role to Admin, save, and confirm the user has no groups afterwards. Change another user back from Admin to Reviewer and confirm save is blocked until at least one group is selected.

**Acceptance Scenarios**:

1. **Given** the admin is on the user-create form with role=Reviewer and zero groups selected, **When** the admin submits, **Then** the form rejects the submission with a validation error and no user is created.
2. **Given** the admin is on the user-create form with role=Applicant and at least one group selected, **When** the admin submits, **Then** the user is created with those memberships.
3. **Given** an existing Reviewer with two groups, **When** the admin opens edit and removes one (leaving one), **Then** save succeeds and the user retains the remaining group.
4. **Given** an existing Reviewer being changed to role=Admin, **When** the admin saves, **Then** all prior group memberships are discarded and the group selector is hidden.
5. **Given** an existing Admin being changed to role=Reviewer, **When** the admin saves without selecting any group, **Then** save is blocked with a validation error.

---

### User Story 3 - Reviewer sees only applicants from shared groups (Priority: P1)

A reviewer signs in. Every reviewer-facing surface (queue, application detail, signing inbox, applicant/application search) shows only applicants whose group set intersects the reviewer's group set. Direct-URL access to an out-of-scope application returns 403. Admins bypass all of this and see everything.

**Why this priority**: This is the user-visible value of the feature. Story 1 and Story 2 produce data; Story 3 produces the experience. It is independently testable: with seeded users in known groups, every surface can be exercised end-to-end.

**Independent Test**: Seed three groups, one applicant each in distinct groups, two reviewers (one in group A, one in groups A+B). Sign in as each reviewer and verify the queue, the search results, the signing inbox, and direct-URL access produce the expected scoped view. Sign in as admin and verify all surfaces show every applicant.

**Acceptance Scenarios**:

1. **Given** a reviewer assigned to group "Norte" only and three applicants assigned to "Norte", "Sur", "Norte+Sur" respectively, **When** the reviewer opens the queue, **Then** they see exactly the "Norte" applicant and the "Norte+Sur" applicant.
2. **Given** a reviewer assigned to "Norte" only and an application owned by an applicant in "Sur", **When** the reviewer requests the application detail URL directly, **Then** the response is 403.
3. **Given** an applicant signed in as themselves, **When** they open their own application, **Then** they see it regardless of their own group set (including the empty case).
4. **Given** an admin signed in (with no group memberships), **When** they open the queue, the signing inbox, the applicant search, and any application detail page, **Then** they see every applicant and every application.
5. **Given** a reviewer with zero group memberships (e.g., after their only group was deleted), **When** they open any reviewer-facing surface, **Then** the surface is empty and any direct-URL detail access returns 403, but the user can still log in.

---

### User Story 4 - Group deletion cascades cleanly (Priority: P2)

When an admin deletes a group, every membership row that references it is removed. Users formerly in that group keep working: if they still have other groups, those still apply; if the deletion leaves them with zero groups, the system permits this state without locking the account. Admin sees affected users surfaced in the admin user list with their (possibly empty) group set.

**Why this priority**: Group deletion is part of normal admin lifecycle. It is independently testable but lower than the visibility behavior because it is a destructive admin action and not the main user value.

**Independent Test**: Seed users in two groups. As admin, delete one group. Verify the other group's memberships are unaffected, deleted-group memberships are gone, no users are deleted, and a user who only belonged to the deleted group now appears in the admin user list with zero groups and an empty reviewer queue.

**Acceptance Scenarios**:

1. **Given** group "Norte" has three members and group "Sur" has two members (one user is in both), **When** the admin deletes "Norte", **Then** the two "Sur" memberships are unchanged, the three "Norte" memberships are gone, the dual-group user keeps "Sur", and no user record is deleted.
2. **Given** a Reviewer whose only group was just deleted, **When** they sign in and open the queue, **Then** the queue is empty and they remain signed-in.
3. **Given** an Applicant whose only group was just deleted, **When** the admin views the applicant list, **Then** the applicant is shown with zero groups; admin can still see and act on the applicant's application.

---

### Edge Cases

- A non-admin user ends up with zero groups after a cascade-delete. The system does not block login or auto-reassign; the reviewer queue is simply empty and detail-page authorization will deny.
- An admin demotes themselves (or another Admin) to Reviewer in the same edit. Save is blocked unless the form also contains at least one group selection at submit time.
- An admin promotes a Reviewer to Admin who currently has groups. On save, all prior memberships are discarded and not restorable except by manual reassignment after a future demotion.
- A reviewer is in the middle of working when an admin renames their group. On the next request, the reviewer's queue continues to function (rename does not change identity).
- A reviewer is in the middle of working when an admin removes their last group. On the next request the queue is empty; in-flight detail page requests for out-of-scope applicants are denied.
- Two admins concurrently edit the same user's groups. Last write wins, enforced via the existing optimistic-concurrency token on the user record (per the constitution's Quality Gate on optimistic concurrency). No additional concurrency control is added.
- Empty or whitespace-only group name on create or rename is rejected with a validation error.
- Non-admin attempts a group-management endpoint directly. Request returns 403.
- Admin saves a user with role=Admin and group selections (e.g., via crafted form payload). Memberships are silently cleared so the invariant "Admin has no groups" holds.

## Requirements *(mandatory)*

### Functional Requirements

#### Group entity

- **FR-001**: A group MUST have a non-empty name that is unique across all groups, compared case-insensitively.
- **FR-002**: Only users with the Admin role MAY create, rename, or delete groups. All other roles, including Reviewer, MUST receive 403 from group-management endpoints.
- **FR-003**: The admin group-list view MUST show every group together with its current member count.

#### Group lifecycle

- **FR-004**: Deleting a group MUST cascade by removing every membership row that references it; the user records themselves MUST NOT be deleted.
- **FR-005**: A non-admin user MAY end up with zero groups after a cascade delete. The system MUST NOT auto-assign a replacement group, MUST NOT block login, and MUST NOT delete the user record.
- **FR-006**: Renaming a group MUST preserve every existing membership row; only the displayed name changes.

#### User-group assignment (admin user CRUD)

- **FR-007**: The admin user-create form MUST require at least one group when the selected role is Applicant or Reviewer. Submission with zero groups for those roles MUST be rejected with a validation error.
- **FR-008**: The admin user-edit form MUST enforce the same "at least one group" rule at submit time when the resulting role is Applicant or Reviewer.
- **FR-009**: The Admin role MUST never carry group memberships. The group selector MUST be hidden when the form's selected role is Admin, and any pre-existing memberships MUST be discarded on save when the resulting role is Admin.
- **FR-010**: The group selector MUST be a multi-select control listing every existing group.

#### Reviewer scoping (visibility filter)

- **FR-011**: The reviewer queue MUST display only those applications whose applicant shares at least one group with the signed-in reviewer.
- **FR-012**: The application detail page MUST return 403 to a reviewer who has zero group overlap with the application's applicant.
- **FR-013**: The signing inbox (signed-funding-agreement queue) MUST apply the same group-overlap filter as the reviewer queue.
- **FR-014**: Reviewer-facing applicant and application search MUST apply the same group-overlap filter. The reviewer queue ships with a text-search input (matched against the applicant's name and legal id); the existing status-based filters (`All`, `AwaitingMe`, `Aging`, `SentBack`, `Appealing`) compose with both the search term and the group-overlap predicate. No separate search controller exists today; the queue is the single reviewer-facing listing surface.
- **FR-015**: A user with the Admin role MUST be exempt from FR-011 through FR-014 and see every applicant and every application on every reviewer-facing surface.

#### Applicant own access

- **FR-016**: An applicant MUST always be able to access their own application regardless of whether they currently have group memberships.

### Non-Functional Requirements

- **NFR-001**: The group-overlap filter MUST be applied at the data-store query level for every listing surface (reviewer queue, signing inbox, search). In-memory post-filtering of an unscoped result set is not acceptable.
- **NFR-002**: Detail-page authorization (FR-012) MUST be enforced server-side. URL tampering or client-side manipulation MUST NOT bypass the check.
- **NFR-003**: Changes to a user's group memberships MUST take effect on the next request from that user. The user MUST NOT be required to sign out and back in.
- **NFR-004**: All admin-area copy added by this feature (group CRUD screens, the user-form group selector, validation messages) MUST be available in es-CR per the existing localization feature.
- **NFR-005**: Group create, rename, and delete events, and changes to a user's group memberships, MUST be recorded with the acting administrator and a timestamp, using whatever audit mechanism the project standardizes on.

### Key Entities

- **Group**: A named partition used to scope reviewer access. Attributes: identity, unique name. A group has zero or more member users.
- **User-Group membership**: A many-to-many relationship between a user (Applicant or Reviewer) and a Group. An admin user has no memberships.
- **ApplicationUser** (existing): The identity record. Carries the user's group memberships directly.
- **Applicant** (existing): A non-admin user record linked to an ApplicationUser by user id. The applicant's "groups" are derived through the link; there is no separate applicant-side membership.
- **Application** (existing): The funding submission. Visibility from the reviewer side is decided by comparing the applicant's groups (via the linked ApplicationUser) against the reviewer's groups.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A reviewer in group "Norte" only sees the applicants in "Norte" on every reviewer surface; a reviewer in groups "Norte" and "Sur" sees the union of those applicants.
- **SC-002**: A signed-in admin sees 100% of applicants and 100% of applications on the queue, the signing inbox, and applicant search, and can open any application's detail page.
- **SC-003**: An attempt to save the admin user-create or user-edit form with role=Applicant or Reviewer and zero groups is blocked with an inline validation error in 100% of cases.
- **SC-004**: After deleting a group with N members, exactly N membership rows for that group are removed and zero user records are deleted; users formerly in only that group show an empty queue and remain able to sign in.
- **SC-005**: A reviewer requesting the detail URL of an out-of-scope application receives 403 in 100% of cases; an admin requesting the same URL receives 200 in 100% of cases.
- **SC-006**: A change to a user's group memberships is reflected on that user's next page load without requiring sign-out.

## Assumptions

- The system is not yet in production; existing user and application data may be dropped and re-seeded as part of bringing this feature in. No data-migration plan is needed.
- The existing role model (Applicant, Reviewer, Admin) and the existing claims transformation that lets an admin act as a reviewer remain unchanged.
- The applicant-as-user link (the `Applicant` entity referencing an `ApplicationUser` by user id) is the canonical way an applicant obtains group memberships. Applicants do not carry a separate group list of their own.
- The existing applicant-owner authorization on application detail pages remains in effect; the group filter is additive on the reviewer side and does not restrict applicants from their own data.
- Demo seed data will include a small set of named groups (working assumption: "Norte", "Sur", "Centro") and seed users distributed across them so all reviewer-facing surfaces are exercisable on first run.
- Audit recording (NFR-005) will reuse whatever audit mechanism the project standardizes on; if none exists, a minimal one will be added during implementation. The choice does not affect feature scope.

## Out of Scope

- Hierarchical groups (parent/child).
- Multi-dimensional tagging across multiple group dimensions (e.g., region × sector).
- Self-service group selection by applicants on signup.
- Per-application reviewer assignment as a separate routing mechanism.
- Group-based filters in admin reports (admin sees everything; reports remain unscoped).
- Migration of existing data from a prior schema (system is non-production).
- Per-group quotas, rate limits, or workload balancing.
- Cross-group transfer workflows beyond editing a user's group set on the user-edit form.

## Dependencies

- The existing identity model: `ApplicationUser` and the three-role catalog (Applicant, Reviewer, Admin) including the admin-implies-reviewer claims transformation.
- The existing `Applicant` entity and its `UserId` link to `ApplicationUser`, which is the path by which applicants inherit group membership.
- The existing application authorization helpers that decide reviewer access at the entity level — these are extended to consider group overlap.
- The existing admin user CRUD surface (admin user list, create form, edit form), which gains a group selector and the validation rules above.
- The existing es-CR localization feature: all new copy participates in it.

## Open Questions

- The audit mechanism backing NFR-005 (reuse vs. add) is deferred to the implementation plan.
- The exact set of seed group names for demo data is deferred to the implementation plan; "Norte", "Sur", "Centro" is the working assumption.
