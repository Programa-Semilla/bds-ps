# Admin Routes — Contracts

**Feature**: 021-feedback-session-may13 | **Surface**: ASP.NET MVC `/Admin/*`

All routes require role `Administrator` unless otherwise noted. SupplierAdmin scope handled in `supplier-admin-routes.md` below.

## Processes

| Verb | Route | Action | Auth | Notes |
|------|-------|--------|------|-------|
| GET | `/Admin/Processes` | `Index` | Admin | List with status filter (Active / Closed). Empty-state copy: *"Aún no hay procesos. Cree el primero."* |
| GET | `/Admin/Processes/Create` | `Create` | Admin | Form: `Name`. |
| POST | `/Admin/Processes/Create` | `Create` | Admin | 422 on validation; 302 → Index on success. Emits `ProcessCreated`. |
| GET | `/Admin/Processes/{id}` | `Details` | Admin | Shows ProcessPlantilla snapshot, attached Groups, stage-window overrides. |
| POST | `/Admin/Processes/{id}/AssignPlantilla` | `AssignPlantilla` | Admin | Body: `plantillaId`. 422 if Plantilla has zero ImpactTemplates or Process already has a snapshot. Emits `PlantillaAssignedToProcess`. |
| POST | `/Admin/Processes/{id}/StageOverride` | `OverrideStageWindow` | Admin | Body: `stageKind`, `days?` (null reverts). Emits `ProcessStageWindowOverridden`. |
| POST | `/Admin/Processes/{id}/Close` | `Close` | Admin | 422 with offending PublicCode list if active Applications exist. Emits `ProcessClosed`. |

## Plantillas

| Verb | Route | Action | Auth | Notes |
|------|-------|--------|------|-------|
| GET | `/Admin/Plantillas` | `Index` | Admin | List active + archived. |
| GET | `/Admin/Plantillas/Create` | `Create` | Admin | Form: Name, MinimumQuotationsPerItem, ImpactTemplate multi-select, RequiredFieldFlags multi-check. |
| POST | `/Admin/Plantillas/Create` | `Create` | Admin | |
| GET | `/Admin/Plantillas/{id}/Edit` | `Edit` | Admin | Banner: *"Las ediciones no afectan procesos ya asignados."* |
| POST | `/Admin/Plantillas/{id}/Edit` | `Edit` | Admin | |
| POST | `/Admin/Plantillas/{id}/Detach/{processId}` | `Detach` | Admin | Force-detach requires `reason` body + admin confirmation. Emits `PlantillaForceDetached`. |
| POST | `/Admin/Plantillas/{id}/Archive` | `Archive` | Admin | Blocks if used by active ProcessPlantilla snapshots. |

## Users (extended)

| Verb | Route | Action | Auth | Notes |
|------|-------|--------|------|-------|
| GET | `/Admin/Users` | `Index` | Admin | Group filter rendered as two-level cascade Process → Group. |
| GET | `/Admin/Users/{id}/Edit` | `Edit` | Admin | Editable: `Email`, `Role`, `Groups` (multi-select), `CodigoPersonal`. Read-only on this surface: none. |
| POST | `/Admin/Users/{id}/Edit` | `Edit` | Admin | Reviewer assignments stored in `UserGroupMembership` (existing). |

## Reports

| Verb | Route | Action | Auth | Notes |
|------|-------|--------|------|-------|
| GET | `/Admin/Reports/*` | (existing) | Admin | Group filter cascades Process → Group (FR-034). Reports surface unchanged otherwise. |

## Dashboard

| Verb | Route | Action | Auth | Notes |
|------|-------|--------|------|-------|
| GET | `/Admin` | `Index` | Admin | Capability-complete dashboard from spec 017. Adds two new tiles (FR-032): *Personas activas*, *Fondos entregados*. Removes pending-quotation tile (moved to Reviewer dashboard per FR-033). Existing 4 action KPIs preserved. |

## Public Landing — admin uploads

| Verb | Route | Action | Auth | Notes |
|------|-------|--------|------|-------|
| GET | `/Admin/PublicLanding` | `Index` | Admin | Shows current Reglamento + Ejemplo slot states. |
| POST | `/Admin/PublicLanding/UploadReglamento` | `UploadReglamento` | Admin | PDF only; stored via `IObjectStorage` category `public-landing-files`. |
| POST | `/Admin/PublicLanding/UploadEjemplo` | `UploadEjemplo` | Admin | |
| POST | `/Admin/PublicLanding/Clear/{slot}` | `Clear` | Admin | `slot ∈ { reglamento, ejemplo }`. |

## SupplierAdmin scope

| Verb | Route | Action | Auth | Notes |
|------|-------|--------|------|-------|
| GET | `/Admin/Suppliers` | `Index` | Admin OR SupplierAdmin | Default sort `LastUsedAt DESC`. Filter by Process. Autocomplete on Name OR CédulaJurídica (P95 ≤ 300 ms). |
| GET | `/Admin/Suppliers/Create` | `Create` | Admin OR SupplierAdmin | |
| POST | `/Admin/Suppliers/Create` | `Create` | Admin OR SupplierAdmin | |
| GET | `/Admin/Suppliers/{id}/Edit` | `Edit` | Admin OR SupplierAdmin | |
| POST | `/Admin/Suppliers/{id}/Edit` | `Edit` | Admin OR SupplierAdmin | |
| POST | `/Admin/Suppliers/{id}/ToggleCompliance` | `ToggleCompliance` | Admin OR SupplierAdmin | Single source of truth (FR-010). |
| GET | `/Admin/SupplierBranches/Create` | `Create` | Admin OR SupplierAdmin | Includes ContactPersonName, Province → Cantón cascade. |
| POST | `/Admin/SupplierBranches/Create` | `Create` | Admin OR SupplierAdmin | |

### Denied surfaces (SupplierAdmin)

When a user holds ONLY the SupplierAdmin role and reaches any other `/Admin/*` route:

- Response: HTTP 403, Tabler-styled 403 view.
- Side effect: `AdminAuditEvent` row of kind `SupplierAdminDeniedAccess` written with `Route`, `UserId`, `OccurredAt`.

## API (admin-side helpers)

| Verb | Route | Action | Auth | Notes |
|------|-------|--------|------|-------|
| GET | `/api/cantons?provinceId={id}` | `CantonsApiController.Index` | Anonymous | Returns `[{ id, name }]`. Cache 1h. |
| GET | `/api/suppliers/search?q={term}` | `SuppliersApiController.Search` | Admin OR SupplierAdmin | Autocomplete on Name + CédulaJurídica; P95 ≤ 300 ms; results capped 25. |

## AdminAuditEvent new kinds

```text
ProcessCreated
ProcessClosed
ProcessStageWindowOverridden
PlantillaAssignedToProcess
PlantillaForceDetached
SupplierAdminDeniedAccess
```

Each row carries `EventKind`, `ActorUserId`, `OccurredAt`, `Payload` (free-text JSON: e.g. `{ "processId": 12, "stageKind": "facturacion", "days": 45 }`).
