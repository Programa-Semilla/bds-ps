# Audit Events — Contracts

**Feature**: 021-feedback-session-may13 | **Source entity**: `AdminAuditEvent` (extended from spec 016)

All event rows share the existing schema:

```text
AdminAuditEvents
├── Id BIGINT IDENTITY PK
├── EventKind NVARCHAR(60) NOT NULL     ← new kinds below
├── ActorUserId NVARCHAR(450) NOT NULL FK → AspNetUsers.Id
├── OccurredAt DATETIME2(0) NOT NULL DEFAULT SYSUTCDATETIME()
├── Payload NVARCHAR(MAX) NULL          ← JSON, free-text
└── (existing columns from spec 016 preserved)
```

## New event kinds

| EventKind | Trigger | Payload shape |
|-----------|---------|---------------|
| `ProcessCreated` | `Process.Create` succeeded | `{ "processId": int, "name": string }` |
| `ProcessClosed` | `Process.Close()` succeeded | `{ "processId": int, "closedAt": iso-datetime }` |
| `ProcessStageWindowOverridden` | `Process.OverrideStageWindow` succeeded | `{ "processId": int, "stageKind": "solicitud"|"revision"|"facturacion", "days": int | null }` |
| `PlantillaAssignedToProcess` | `Plantilla.AssignTo(process)` succeeded; snapshot row created | `{ "processId": int, "plantillaId": int, "processPlantillaId": int }` |
| `PlantillaForceDetached` | Force-detach with admin reason | `{ "processId": int, "plantillaId": int, "reason": string }` |
| `SupplierAdminDeniedAccess` | `SupplierAdminDeniedAttribute` 403 path | `{ "route": string, "method": "GET"|"POST", "userAgent": string \| null }` |

## Read-side surface

- Admin audit log view (existing spec 016 surface) renders new kinds with localized labels via `IAdminAuditEventCopyProvider`. New label keys land in `es-CR.resx`.
- Read API (existing): `GET /Admin/AuditEvents` filterable by EventKind; new kinds appear in the filter dropdown automatically.

## Invariants

- **At-least-once**: Every state mutation that triggers an event must write the audit row in the same transaction as the mutation (or before the failing transaction commits, for deny-access cases — see below).
- **Deny-access audit row**: For `SupplierAdminDeniedAccess`, the row is written even though the underlying admin action did not execute. Filter writes the row, then returns 403.
- **PII**: No raw passwords, no reset-token plaintext, no email body content. Payloads are structured identifiers only.
- **Idempotency**: `PlantillaAssignedToProcess` is one-per-Process (one-to-one cardinality OQ-1). Subsequent attempts return 422 from the controller before the event is written.
