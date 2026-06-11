# Phase 1 Data Model: Admin — Edit Process Name

**No schema change.** This feature mutates one existing field on one existing entity and adds one
audit-event enum value. No tables, columns, indexes, or relationships are added or altered.

## Entity: Process (modified — behavior only, no new persisted state)

`src/FundingPlatform.Domain/Entities/Process.cs`

| Field | Type | Change | Notes |
|---|---|---|---|
| `Name` | `string` (≤120) | **mutated** | Already mutable via `Process.Rename(string)` (exists). Required, trimmed, ≤ `Process.MaxNameLength` (120). Unique across catalog via `UX_Processes_Name`. |
| `RowVersion` | `byte[]` | unchanged | Existing rowversion concurrency token (`IsRowVersion()`) — guards the rename UPDATE automatically. |

**Domain method (already present — no change expected):**
`Process.Rename(string newName)` — validates via `ValidateName` (required / trimmed / ≤120),
no-ops when the trimmed value equals the current `Name` (satisfies FR-006 / SC-005). Has **no**
`ProcessClosedException` guard, which is exactly the desired behavior (FR-002: rename allowed at
any status). Do **not** add a Closed guard.

**Validation rules (from `ValidateName`):**
- `null` / empty / whitespace-only → `ArgumentException("Process name is required.")`
- `> 120` chars after trim → `ArgumentException("Process name must be 120 characters or fewer.")`
- leading/trailing whitespace trimmed before persist + comparison

## Entity: AdminAuditEvent (extended)

`src/FundingPlatform.Domain/Entities/AdminAuditEvent.cs`

| New constant | String value | Target derivation |
|---|---|---|
| `ProcessRenamed` | `"process.renamed"` | `process.` prefix → classified as Process target by `AdminAuditEventWriter` (existing logic) |

**Audit payload** (JSON, written via `IAdminAuditEventWriter.WriteAsync`):
```json
{ "processId": <int>, "oldName": "<string>", "newName": "<string>" }
```
`oldName`/`newName` satisfy SC-001 ("audit row written with the actor and old/new name"). Actor is
the `actorUserId` argument (current admin), same as every other Process audit write.

## Application command (new record)

`src/FundingPlatform.Application/Processes/IProcessService.cs`

```csharp
/// <summary>Spec 030 — record carrying the rename payload.</summary>
public sealed record RenameProcessCommand(int ProcessId, string NewName);
```

Mirrors `ReassignProcessFundCommand(int ProcessId, int FundId)`.

## State / lifecycle

No state machine change. Rename is allowed in **both** `ProcessStatus.Active` and
`ProcessStatus.Closed`. Status, `ClosedAt`, Groups, Plantilla snapshot, and Fund anchor are
untouched by a rename.
