# Contract: Admin Groups CRUD

Internal MVC routes. Authorization: every action requires the `Admin` role.
Any non-admin caller — including authenticated reviewers and applicants —
MUST receive HTTP 403 (FR-002).

All copy returned in HTML and validation messages MUST be in es-CR
(NFR-004). The es-CR strings live in `AdminGroupsResources`.

---

## GET `/Admin/Groups`

### Purpose

List the group catalog with member counts (FR-003).

### Authorization

`[Authorize(Roles = "Admin")]`. Non-admin → 403.

### Request

No body. No query string parameters.

### Response — 200 OK

Renders `Views/Admin/Groups/Index.cshtml` bound to:

```csharp
public sealed class AdminGroupsIndexViewModel
{
    public IReadOnlyList<AdminGroupRow> Groups { get; init; }
}

public sealed record AdminGroupRow(int Id, string Name, int MemberCount);
```

Sort: `Name` ascending.

### Response — 403 Forbidden

When the caller is authenticated but not in role `Admin`.

---

## GET `/Admin/Groups/Create`

### Purpose

Render the empty create form.

### Authorization

`[Authorize(Roles = "Admin")]`.

### Response — 200 OK

Renders `Views/Admin/Groups/Create.cshtml` bound to a fresh
`AdminGroupCreateViewModel`.

---

## POST `/Admin/Groups`

### Purpose

Create a new group (FR-001, FR-002).

### Authorization

`[Authorize(Roles = "Admin")]`. `[ValidateAntiForgeryToken]`.

### Request body (form-encoded)

```
Name=string
__RequestVerificationToken=...
```

### Validation

| Rule | Error message key |
|---|---|
| `Name` non-empty after trim | `AdminGroupsResources.NameRequired` |
| `Name` length ≤ 100 after trim | `AdminGroupsResources.NameTooLong` |
| `Name` not already in use (case/accent-insensitive) | `AdminGroupsResources.NameAlreadyInUse` |

All validation errors MUST be collected and returned together (constitution
quality gate). The form re-renders with `ModelState` errors visible inline.

### Response — 302 Redirect

On success: `Location: /Admin/Groups`. An `AdminAuditEvent` row is written
with `Action = "group.create"`, `TargetType = "group"`,
`TargetId = <new-id-as-text>`, `PayloadJson = {"name":"<trimmed>"}`.

### Response — 200 OK (form re-render)

When validation fails, returns 200 with the form re-rendered and
`ModelState` populated.

---

## GET `/Admin/Groups/{id:int}/Edit`

### Purpose

Render the rename form.

### Authorization

`[Authorize(Roles = "Admin")]`.

### Response — 200 OK

Renders `Views/Admin/Groups/Edit.cshtml` bound to:

```csharp
public sealed class AdminGroupEditViewModel
{
    public int Id { get; init; }
    [Required, StringLength(100, MinimumLength = 1)]
    public string Name { get; set; } = "";
}
```

### Response — 404 Not Found

When `id` does not exist.

---

## POST `/Admin/Groups/{id:int}/Edit`

### Purpose

Rename a group, preserving every existing membership row (FR-006).

### Authorization

`[Authorize(Roles = "Admin")]`. `[ValidateAntiForgeryToken]`.

### Request body (form-encoded)

```
Id=<int>
Name=<string>
__RequestVerificationToken=...
```

### Validation

Same rules as `POST /Admin/Groups`. The uniqueness check MUST exclude the
group's own current row (renaming `Norte` → `Norte` is allowed and is a
no-op).

### Response — 302 Redirect

On success: `Location: /Admin/Groups`. Audit:
`Action = "group.rename"`, `PayloadJson = {"old":"<old>","new":"<new>"}`.
No-op renames also write an audit row to keep the trail honest.

### Response — 404 Not Found

When `id` does not exist.

### Response — 200 OK (form re-render)

When validation fails.

---

## POST `/Admin/Groups/{id:int}/Delete`

### Purpose

Delete the group and cascade through `UserGroupMemberships` (FR-004,
FR-005).

### Authorization

`[Authorize(Roles = "Admin")]`. `[ValidateAntiForgeryToken]`.

### Request body (form-encoded)

```
Id=<int>
__RequestVerificationToken=...
```

### Behavior

- All `UserGroupMembership` rows referencing the group are removed (DB
  cascade).
- Affected `ApplicationUser` rows are NOT deleted (FR-005).
- An `AdminAuditEvent` is written with `Action = "group.delete"`,
  `TargetId = <id>`, `PayloadJson = {"name":"<deleted-name>","memberCountBefore":<n>}`.

### Response — 302 Redirect

On success: `Location: /Admin/Groups`.

### Response — 404 Not Found

When `id` does not exist.
