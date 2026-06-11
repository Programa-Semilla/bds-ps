# Phase 0 Research: Admin — Edit Process Name

All seams were verified during brainstorming (see `implementation-notes.md`). This file resolves
the one open item carried from the spec review and records the key reuse decisions.

## R-1 — Optimistic concurrency on the rename path (RESOLVED — the only open question)

**Decision**: Follow the existing `ProcessService.ReassignFundAsync` pattern exactly — load the
Process, call `Process.Rename(newName)`, `SaveChangesAsync`. No hidden RowVersion round-trip in
the form.

**Rationale**: `ProcessConfiguration` already maps `builder.Property(p => p.RowVersion).IsRowVersion()`
(SQL Server rowversion concurrency token). EF Core therefore appends `RowVersion` to the `WHERE`
clause of the generated `UPDATE`, so the in-request load→mutate→save is automatically guarded; a
row changed out from under the request raises `DbUpdateConcurrencyException`. The duplicate-name
race across requests is independently caught by the `UX_Processes_Name` unique index
(`DbUpdateException`). A true cross-page-load lost-update on a single admin-only field is accepted
as out of scope — this is exactly the posture of the sibling `ChangeFund`/`ReassignFundAsync`
mutation, and tightening it (hidden RowVersion field + `OriginalValues` round-trip) is YAGNI for
this field (constitution Principle VI).

**Handling**: Let `DbUpdateException` (duplicate) surface to the controller for the es-CR
"Ya existe un proceso con ese nombre." mapping. Optionally catch `DbUpdateConcurrencyException`
and surface a generic es-CR error toast ("El proceso fue modificado por otra persona; intente de
nuevo."); low priority since the window is request-scoped.

**Alternatives considered**:
- *Hidden RowVersion field + set `OriginalValues`* — true cross-request optimistic concurrency,
  but no other Process mutation does this and the risk (two admins renaming the same Process
  simultaneously) is negligible. Rejected as over-engineering.
- *App-layer pre-check `AnyAsync(p => p.Name == newName)`* — rejected; redundant with the unique
  index, racy on its own, and not what `CreateAsync` does (CreateAsync relies on the index +
  `DbUpdateException`). Stay consistent.

## R-2 — Audit event naming (RESOLVED)

**Decision**: Add `AdminAuditEvent.ProcessRenamed` → string `"process.renamed"`.

**Rationale**: Matches the existing convention (`process.created`, `process.closed`,
`process.fund_reassigned`, `process.stage_window.overridden`). The `process.` prefix makes the
existing `AdminAuditEventWriter` target-derivation classify it as a Process target automatically.
Payload mirrors the others: `{ processId, oldName, newName }` (old/new satisfies SC-001 audit
content). Write via `IAdminAuditEventWriter.WriteAsync(...)` inside the same `SaveChangesAsync`
unit of work — same shape as `ReassignFundAsync`.

## R-3 — Controller error-mapping parity with Create (RESOLVED)

**Decision**: The `Rename` action maps exceptions exactly like `Create`/`ChangeFund`:
- `ArgumentException` (empty/whitespace/over-length from `Process.Rename`→`ValidateName`) →
  `ModelState.AddModelError` with the message → re-render Details with the inline error.
- `DbUpdateException` (unique-index violation) → inline error "Ya existe un proceso con ese
  nombre." (verbatim reuse from `Create`).
- `KeyNotFoundException` (unknown id) → `NotFound()` (404).
- Success → `TempData["SuccessMessage"] = "Nombre del proceso actualizado."` → redirect to
  `Details`.

**Rationale**: Consistency with the established admin controller idiom; reuses the exact es-CR
copy already shipped for the create path so there is one duplicate-name string, not two.

## R-4 — Inline-form rendering surface (RESOLVED)

**Decision**: Render the Name edit as a card at the top of `Details.cshtml` (above or beside the
Fund card), `method="post" asp-action="Rename" asp-route-id="@detail.Id"`, antiforgery token,
pre-filled `<input name="newName" value="@detail.Name" maxlength="120" required>`, submit button.
Unlike the Fund/stage-window/Plantilla/Groups blocks, it renders **regardless** of
`detail.Status` (FR-002). Add `data-testid` hooks: `admin-process-rename-form`,
`admin-process-rename-input`, `admin-process-rename-submit`, and surface the inline ModelState
error near the input.

**Rationale**: Mirrors the verified Fund-card pattern (`Details.cshtml:49-75`); the only
deviation is "show when Closed," which is the explicit user decision.

## Open NEEDS CLARIFICATION

None. All resolved.
