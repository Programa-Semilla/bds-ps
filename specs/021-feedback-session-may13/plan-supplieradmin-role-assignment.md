# Plan — SupplierAdmin role-assignment via Admin Users UI (bugfix increment)

**Parent spec**: `specs/021-feedback-session-may13/spec.md` — US3, FR-007.
**Scope**: Close the implementation gap that prevents an Admin from assigning the `SupplierAdmin` role through the standard `/Admin/Users` Create/Edit form. Today the role is only assignable via the dev-only `AccountController.AssignRole` endpoint, which means production admins cannot provision a SupplierAdmin without a database edit.

**Branch**: `fix/021-supplieradmin-role-assignment`.

## Problem

Spec 021 FR-007 introduced the `SupplierAdmin` role and its `[SupplierAdminOnly]` / `[SupplierAdminDenied]` authorization plumbing, plus a dev provisioning shortcut (`AccountController.AssignRole`). The standard admin user-management surface (`AdminUsersController.Create` / `Edit`) was never extended to expose the new role. Three independent layers all hardcode the same three legacy roles:

| Layer | File | Symptom |
|---|---|---|
| View — Create | `src/FundingPlatform.Web/Views/Admin/Users/Create.cshtml:8` | `var roles = new[] { "Applicant", "Reviewer", "Admin" };` — dropdown missing the option. |
| View — Edit | `src/FundingPlatform.Web/Views/Admin/Users/Edit.cshtml:8` | Same hardcoded array. |
| Service — allowed roles | `src/FundingPlatform.Infrastructure/Identity/UserAdministrationService.cs:19` | `AllowedRoles = [Applicant, Reviewer, Admin]`. Even if the post body included `SupplierAdmin`, `ValidateRoleAndLegalId` rejects with `"Role must be Applicant, Reviewer, or Admin."` |
| Service — primary-role pick | Same file, `SelectPrimaryRole` (line ~698) | Priority list omits SupplierAdmin → an existing SupplierAdmin user appears with an empty role in the list / edit screen. |

Consequence: there is no production path to grant the `SupplierAdmin` role. The capability fails Acceptance Scenario AS-3.1 ("a user is provisioned with role *SupplierAdmin* only…").

## Decisions (recorded ahead of implementation)

**D1. SupplierAdmin requires NO group membership.** Parity with `Admin`. FR-007 scopes SupplierAdmin to the global supplier catalog — `Process` / `Group` does not apply. The Create/Edit form MUST hide the group selector when role = SupplierAdmin (same as Admin), and `RoleRequiresGroups` MUST return `false` for SupplierAdmin. `NormalizeGroupIdsForRole` strips any incoming group ids when role = SupplierAdmin (defensive — the UI will not submit them, but the service is the boundary).

**D2. Primary-role priority is Admin > Reviewer > SupplierAdmin > Applicant.** Matches the already-shipped `AccountController.BuildProfileViewModelAsync` priority list, so the admin Users list and the profile screen rank dual-role users consistently.

**D3. Role label is "Administrador de proveedores".** Matches the profile-screen label already shipped in `AccountController.BuildProfileViewModelAsync` so admin-area users see the same Spanish copy on both surfaces.

**D4. No schema change, no new migration, no dacpac change.** The `SupplierAdmin` `IdentityRole` row is already seeded by `RoleSeeder` for spec 021; this work only widens the production CRUD path.

**D5. Last-administrator protection unchanged.** The existing `LastAdministratorException` guards only the `Admin` role (not SupplierAdmin) — that is intentional and unchanged by this fix.

## Changes

### View layer (2 files)

`src/FundingPlatform.Web/Views/Admin/Users/Create.cshtml`
`src/FundingPlatform.Web/Views/Admin/Users/Edit.cshtml`

1. Append `"SupplierAdmin"` to the `roles` array.
2. Add `"SupplierAdmin" => "Administrador de proveedores"` to the `RoleLabel` switch.
3. Extend the inline `<script>` block's `updateVisibility()` so the groups field is hidden when `role === 'Admin' || role === 'SupplierAdmin'`.

### Service layer (1 file)

`src/FundingPlatform.Infrastructure/Identity/UserAdministrationService.cs`

1. New const `private const string SupplierAdminRole = "SupplierAdmin";` next to the existing role constants.
2. Extend `AllowedRoles` to `[ApplicantRole, ReviewerRole, AdminRole, SupplierAdminRole]`.
3. `NormalizeGroupIdsForRole`: also return `Array.Empty<int>()` when role = SupplierAdmin (D1).
4. `RoleRequiresGroups`: unchanged — already returns `false` for SupplierAdmin (only Applicant + Reviewer are listed).
5. `SelectPrimaryRole`: insert a third `if (set.Contains(SupplierAdminRole)) return SupplierAdminRole;` between the Reviewer and Applicant branches (D2).
6. `ValidateRoleAndLegalId` error message: update text to `"Role must be Applicant, Reviewer, SupplierAdmin, or Admin."` so a server-side rejection of an unknown role is accurate.

### Tests (integration)

Add to `tests/FundingPlatform.Tests.Integration/Application/UserAdministrationGroupsTests.cs`:

- `SeedRolesAsync` — extend the seeded role list to include `"SupplierAdmin"`.
- `Create_SupplierAdmin_WithoutGroups_Succeeds_AndDoesNotPersistMemberships` — proves D1 (no group required, no membership rows inserted even if the request body carries some).
- `Create_SupplierAdmin_WithGroups_IgnoresMemberships` — posts non-empty `GroupIds`; asserts the user is created with role SupplierAdmin and zero `UserGroupMemberships` rows.
- `Update_Reviewer_To_SupplierAdmin_RemovesMemberships_AndChangesRole` — covers the role-transition path through `UpdateUserAsync`.
- `SelectPrimaryRole_DualRole_AdminWinsOverSupplierAdmin` — guards D2 via a `ListUsersAsync` call on a user holding both roles.

No new E2E test added: the existing `US3_SupplierAdmin` E2E continues to provision via `AccountController.AssignRole` (still legitimate for dev/test). A future change can flip the E2E to drive the admin form once the form is verified manually.

## Verification

1. `dotnet build FundingPlatform.slnx` clean.
2. `dotnet test tests/FundingPlatform.Tests.Unit` green.
3. `dotnet test tests/FundingPlatform.Tests.Integration --filter UserAdministrationGroupsTests` green (new + existing).
4. Manual: log in as `admin@programa-semilla.test` in the ephemeral E2E env, create a user with role *Administrador de proveedores*, log in as that user, confirm sidebar shows only the supplier entry, confirm `/Admin/Users` returns 403.
5. Per CLAUDE.md delivery bar — final go/no-go is a personally-executed green E2E run.

## Rollback

Single-commit, view + service deltas only — `git revert` restores the prior dropdown.

## Demo seed parity

`IdentityConfiguration.SeedUsersAsync` previously shipped three demo users (Applicant / Reviewer / demo-Admin) but no SupplierAdmin. Added `supplieradmin@programa-semilla.test` / `Demo123!` (Lucía Mora, `1-0001-0004`) so a developer can one-click the role from the login screen without first running the dev-only `/Account/AssignRole` provisioning shortcut. Email matches the `@programa-semilla.test` domain so it sits inside the `Notifications:NonProdAllowlist` default and mail captures in smtp4dev without further config.

`SeedRolesAsync` is also extended to include `"SupplierAdmin"` so the role row is present on paths that bypass the dacpac (in-memory unit/integration runs, fresh local demos without sqlpackage). The dacpac post-deployment script `03_SeedSupplierAdminRole.sql` remains the canonical seed in deployed environments; the C# branch is redundant-but-idempotent there (`RoleExistsAsync` short-circuits).

CLAUDE.md updated so future agents see the four-user demo seed list.

## Out of scope

- E2E rewrite of `US3_SupplierAdmin` to drive the admin form (deferred — provisioning via `AssignRole` is still valid).
- Reset-password / Disable / Enable surfaces — they already work for any authenticated identity row and need no change.
- Reports & sidebar copy — no change.

## Audit

This change does not introduce new `AdminAuditEvent` kinds; the existing user-create / user-update audit rows already cover role assignments and so they will pick up SupplierAdmin transitions without change.
