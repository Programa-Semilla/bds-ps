# Contract: Rename Process

UI contract for the inline Process-rename action on the admin detail page. There is no public
API; the "interface" is the MVC route + form contract plus the service command.

## HTTP route

```
POST /Admin/Processes/{id:int}/Rename
```

**Authorization**: `[Authorize(Roles = "Admin,SupplierAdmin")]` + `[SupplierAdminDenied]`
(inherited from `AdminProcessesController` — same as every other action on the controller).
**Antiforgery**: `[ValidateAntiForgeryToken]` (form posts `@Html.AntiForgeryToken()`).

### Request (form-encoded)

| Field | Type | Required | Notes |
|---|---|---|---|
| `id` (route) | int | yes | Process id |
| `newName` | string | yes | Proposed name; trimmed + validated server-side (≤120) |
| `__RequestVerificationToken` | string | yes | Antiforgery |

### Responses

| Outcome | Status / Result | User-visible |
|---|---|---|
| Success (name changed) | 302 → `GET /Admin/Processes/{id}` | Toast: **"Nombre del proceso actualizado."**; new name on header, breadcrumb, Index list; `process.renamed` audit row written |
| Success (name unchanged — no-op) | 302 → `GET /Admin/Processes/{id}` | No error; **no audit row** (domain `Rename` short-circuits) |
| Invalid name (empty / whitespace / >120) | 200 re-render Details with `ModelState` error | Inline es-CR validation message near the input; name unchanged |
| Duplicate name (another Process) | 200 re-render Details with `ModelState` error | Inline **"Ya existe un proceso con ese nombre."**; name unchanged |
| Unknown Process id | 404 `NotFound()` | Standard 404 |
| Concurrency conflict (optional) | 200 re-render / error toast | es-CR "modified by someone else" message (low priority, R-1) |

### Controller exception mapping (mirrors `Create` / `ChangeFund`)

```
ArgumentException            -> ModelState.AddModelError(nameof(newName), ex.Message); re-render Details
DbUpdateException            -> ModelState.AddModelError(nameof(newName), "Ya existe un proceso con ese nombre."); re-render Details
KeyNotFoundException         -> NotFound()
DbUpdateConcurrencyException -> (optional) error toast; re-render Details
success                      -> TempData["SuccessMessage"]="Nombre del proceso actualizado."; RedirectToAction(Details, {id})
```

## Service contract

`IProcessService` (new method):

```csharp
/// <summary>Spec 030 — renames the Process. Writes audit event ProcessRenamed.
/// No-ops (no audit) when the trimmed new name equals the current name.
/// Allowed regardless of Process status.</summary>
Task RenameAsync(RenameProcessCommand command, string actorUserId, CancellationToken ct);
```

**Behavior (mirrors `ReassignFundAsync`):**
1. `ArgumentNullException.ThrowIfNull(command)` + `ArgumentException.ThrowIfNullOrWhiteSpace(actorUserId)`.
2. Load Process by id → `KeyNotFoundException` if missing.
3. Capture `oldName = process.Name`.
4. `process.Rename(command.NewName)` (domain validates + may no-op).
5. **If the name was unchanged (no-op): return without writing an audit row** (compare trimmed
   new name to `oldName`, or detect EF "no modifications" — implementer's choice; the audit row
   MUST NOT be written on a no-op per FR-006/SC-005).
6. Else write `AdminAuditEvent.ProcessRenamed` with payload `{ processId, oldName, newName }`.
7. `SaveChangesAsync(ct)` — duplicate name surfaces as `DbUpdateException` (unique index);
   `RowVersion` guards concurrent edits.

## Test contract

- **Unit** (`Process.Rename`): empty/whitespace → throws; 120 ok / 121 throws; trims surrounding
  whitespace; equal-name → no-op (Name unchanged).
- **Integration** (real DB): rename happy path persists + writes one `process.renamed` row;
  rename to an existing other Process's name → `DbUpdateException`, original unchanged, no audit
  row; rename to same name → no audit row.
- **E2E** (Playwright): rename an Active Process (assert header + Index reflect it); rename a
  Closed Process (assert success); duplicate name → inline error, unchanged; empty name → inline
  error, unchanged. Uses `data-testid`: `admin-process-rename-form`, `admin-process-rename-input`,
  `admin-process-rename-submit`.
