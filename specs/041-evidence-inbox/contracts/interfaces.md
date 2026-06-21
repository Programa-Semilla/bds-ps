# Contracts: Funds-Usage Evidence Inbox

**Feature**: 041-evidence-inbox | **Date**: 2026-06-19

## Application interface — `IEvidenceInboxProjection`

`src/FundingPlatform.Application/EvidenceInbox/IEvidenceInboxProjection.cs`

```csharp
public interface IEvidenceInboxProjection
{
    /// Executed applications (AgreementExecuted) whose governing Process is Active,
    /// scoped to the caller (admin short-circuit, else group-overlap), most-recent first,
    /// capped. Empty for a non-admin reviewer with no group memberships.
    Task<IReadOnlyList<EvidenceInboxRowDto>> GetForUserAsync(
        IReviewerScope scope, CancellationToken ct);
}

public sealed record EvidenceInboxRowDto(
    int ApplicationId,
    string ApplicationNumber,
    string ApplicantName,
    string FundName,
    string ProcessName,
    DateTimeOffset ExecutedAtUtc);
```

Implementation `EvidenceInboxProjection` in `src/FundingPlatform.Infrastructure/Persistence/` (mirrors `ReviewerDashboardProjection`), registered in DI alongside the other reviewer projections.

## Web routes

### `EvidenceInboxController` (NEW) — `[Authorize(Roles = "Reviewer,Admin")]`

| Method | Route | Action | Behavior |
|--------|-------|--------|----------|
| GET | `/Evidence` | `Index` | Resolve scope via `IReviewerScopeProvider`; render `EvidenceInbox/Index` with the projection rows. Empty → es-CR empty state (HTTP 200, not error). |

- Applicants never receive the sidebar entry; an applicant hitting `/Evidence` is refused by the role attribute (consistent with other reviewer surfaces). FR-001/FR-008.
- Rows are pre-scoped by the projection (NFR-001); the view does no filtering.

### `FundsUsageEvidenceController` (EDIT) — routes unchanged (`/Applications/{id}/Evidence…`)

| Method | Route | Change |
|--------|-------|--------|
| GET | `/Applications/{id}/Evidence` | `Index` now sets `IsReadOnly` from `IsProcessClosedAsync`. No access change. |
| POST | `…/Evidence/Upload` | If process closed → no mutation, es-CR toast, redirect to `Index` (FR-007). |
| POST | `…/Evidence/{evidenceId}/Note` | If process closed → no mutation, es-CR toast, redirect to `Index` (FR-007). |
| POST | `…/Evidence/{evidenceId}/Delete` | If process closed → no mutation, es-CR toast, redirect to `Index` (FR-007). |
| GET | `…/Evidence/{evidenceId}/Download` | Unchanged — available in read-only mode (FR-006/D7). |

Existing gates (`IsAccessibleAsync` role+group+`AgreementExecuted`, `EvidenceBelongsAsync`) run first on every action; the process-closed check is applied **after** access is confirmed, so out-of-scope callers still get the flat 404 with no disclosure (FR-008). Order matters: never reveal "closed vs. nonexistent" to an unauthorized caller.

## View / markup contracts (E2E testids)

### `Views/EvidenceInbox/Index.cshtml` (NEW)

| Element | `data-testid` | Notes |
|---------|---------------|-------|
| Row | `evidence-inbox-row` + `data-application-number="APP-{id:D5}"` | mirrors `audit-inbox-row` / `reviewer-queue-row` |
| Row link to evidence | `evidence-inbox-open` | href → `/Applications/{id}/Evidence` |
| Empty state | `evidence-inbox-empty` | es-CR friendly message |

### `Views/FundsUsageEvidence/Index.cshtml` + `_EvidenceRow.cshtml` (EDIT)

| Element | Behavior when `IsReadOnly` |
|---------|----------------------------|
| Upload form (`evidence-upload-*`) | hidden |
| Read-only notice | shown — `data-testid="evidence-readonly-notice"`, es-CR copy |
| Edit-note save (`Action_SaveNote`) | hidden; note textarea read-only/disabled |
| Delete button (`Action_Delete`) | hidden |
| Download link | always shown |

### `Views/Shared/_Layout.cshtml` (EDIT)

Add to `operativoEntries`:

```csharp
new("evidence-inbox", "Evidencia de uso de fondos",
    Url.Action("Index", "EvidenceInbox") ?? "/Evidence",
    "ti ti-folder", new[] { "Reviewer", "Admin" }),
```

Slug `evidence-inbox` (stable E2E `data-testid` for the nav item, same convention as `audit-inbox`/`signing-inbox`).

## es-CR copy (new/changed)

| Key | Surface | Example |
|-----|---------|---------|
| `EvidenceInboxResources.Nav` | sidebar label | "Evidencia de uso de fondos" |
| `EvidenceInboxResources.Title` | inbox page title | "Evidencia de uso de fondos" |
| `EvidenceInboxResources.Empty` | empty state | "No hay solicitudes con convenio ejecutado en procesos activos." |
| `FundsUsageEvidenceResources.ReadOnly_Notice` | read-only banner | "El proceso está cerrado. Esta evidencia es de solo lectura; puede consultarla y descargarla, pero no agregar, editar ni eliminar archivos." |
| `FundsUsageEvidenceResources.Error_ProcessClosed` | blocked-mutation toast | "El proceso está cerrado. No se pueden modificar las evidencias." |

## Non-goals (contract level)

- No new API/JSON endpoints; server-rendered MVC only.
- No change to `FundsUsageEvidence` storage, upload size guard, or file-type policy.
- No pagination/search query parameters on `/Evidence`.
