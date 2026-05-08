# Contract: Admin Users Form (Group Selector Additions)

This contract describes the modifications to the existing admin user
create/edit forms (`AdminUsersController`) introduced by spec 016. The base
form (FirstName, LastName, Email, Phone, Role, etc.) is unchanged.

Authorization: `Admin` role only on every endpoint. Non-admin → 403.

---

## Shared additions to both view models

```csharp
public int[] GroupIds { get; set; } = Array.Empty<int>();

// Populated for rendering the selector. Not posted back.
public IReadOnlyList<AdminUserGroupOption> AvailableGroups { get; init; }
    = Array.Empty<AdminUserGroupOption>();

public sealed record AdminUserGroupOption(int Id, string Name);
```

`AvailableGroups` MUST be populated from the `Group` catalog ordered by
`Name` ascending whenever the form is rendered.

---

## GET `/Admin/Users/Create`

### Behavior change

- The view renders a multi-select control listing every `Group` ordered by
  `Name`, bound to `GroupIds`.
- The selector is hidden (CSS class `d-none` plus a conditional `<select>`
  block) when the role dropdown selects `Admin`. Server-side, when the
  posted role is `Admin`, the bound `GroupIds` MUST be ignored.

### Validation rule (added)

When the resulting role is `Applicant` or `Reviewer`, `GroupIds` MUST
contain at least one valid group id (FR-007). Validation message key:
`AdminUsersResources.AtLeastOneGroupRequired`.

When the resulting role is `Admin`, `GroupIds` is ignored even if posted
(FR-009 — Edge Case).

---

## POST `/Admin/Users` (create)

### Request body additions

```
GroupIds=<int>          (multi-valued: the multi-select submits one
                         GroupIds entry per selected option)
```

### Authorization

`[Authorize(Roles = "Admin")]`. `[ValidateAntiForgeryToken]`.

### Validation

All existing validation plus:

| Rule | Error message key |
|---|---|
| At least one group when role ∈ {Applicant, Reviewer} | `AdminUsersResources.AtLeastOneGroupRequired` |
| Every posted group id MUST exist | `AdminUsersResources.GroupNotFound` |

### Behavior

On success:
- The user is created via the existing `UserAdministrationService.CreateUserAsync`.
- A `UserGroupMembership` row is inserted for each selected group when the
  role is `Applicant` or `Reviewer`. When the role is `Admin`, no
  memberships are inserted regardless of posted `GroupIds`.
- An `AdminAuditEvent` row is written with `Action = "user.memberships.update"`,
  `TargetType = "user"`, `TargetId = <new user id>`,
  `PayloadJson = {"added":[…],"removed":[]}`.

### Response — 302 Redirect

`Location: /Admin/Users`.

### Response — 200 OK (form re-render)

When validation fails. All errors collected.

---

## GET `/Admin/Users/{id}/Edit`

### Behavior change

- The view renders the same multi-select, pre-selecting the user's current
  `GroupIds`.
- The selector is hidden when the loaded user's role is `Admin`.
- The hidden `RowVersion` (existing `ConcurrencyStamp`) is rendered
  unchanged.

---

## POST `/Admin/Users/{id}/Edit`

### Request body additions

```
GroupIds=<int>          (multi-valued)
```

### Validation

| Rule | Error message key |
|---|---|
| At least one group when resulting role ∈ {Applicant, Reviewer} | `AdminUsersResources.AtLeastOneGroupRequired` |
| Every posted group id MUST exist | `AdminUsersResources.GroupNotFound` |
| Existing `ConcurrencyStamp` matches | `AdminUsersResources.ConcurrencyConflict` |

### Behavior

The membership update is computed as `(added, removed) = diff(current, posted)`:

- If the resulting role is `Admin`, all current memberships are removed and
  no new ones are inserted (FR-009, edge case "saved with role=Admin and
  group selections").
- If the resulting role is `Applicant` or `Reviewer`, the diff is applied:
  remove rows in `removed`, insert rows in `added`.
- The whole change is one EF Core transaction; the user row's
  `ConcurrencyStamp` is rotated as part of the user save (FR-NFR existing
  optimistic-concurrency convention). A conflict surfaces the
  `ConcurrencyConflict` validation message; the form re-renders with the
  current server state.
- An `AdminAuditEvent` row is written with `Action =
  "user.memberships.update"`, `PayloadJson = {"added":[…],"removed":[…]}`.
  No-op edits do NOT write an audit row.

### Response — 302 Redirect

`Location: /Admin/Users` on success.

### Response — 200 OK (form re-render)

On validation failure (including concurrency conflict).
