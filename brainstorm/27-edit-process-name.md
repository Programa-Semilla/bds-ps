# Brainstorm: Edit Process Name

**Date:** 2026-06-10
**Status:** spec-created
**Spec:** specs/030-edit-process-name/

## Problem Framing

An admin reported that at `/Admin/Processes` there is no way to update the details of an
existing Process. Investigation confirmed: the Process detail page (`/Admin/Processes/{id}`)
already lets an admin change the Fund, override stage windows, assign/detach a Plantilla,
create Groups, and close the Process — but the **Name** has no edit affordance anywhere. The
domain even ships an unused `Process.Rename()` method. So the most basic detail (the name) is
effectively immutable after creation; the only "fix" is to recreate the Process, losing its
Groups, Plantilla snapshot, and history.

## Approaches Considered

### A: Inline name edit on the Details page (chosen)
- Pros: Consistent with every other Process detail, which is already edited inline on this page;
  no new navigation; smallest surface; reuses the Fund/stage-window inline-form pattern, the
  `ReassignFundAsync`/`ChangeFund` service+controller style, and the existing `UX_Processes_Name`
  uniqueness gate. No schema change; domain `Rename()` already exists.
- Cons: Slightly less "conventional CRUD" than a dedicated edit page.

### B: Dedicated `/Admin/Processes/{id}/Edit` page
- Pros: Conventional CRUD; a place to grow if more editable fields are added later; an obvious
  "Editar" button on Index rows.
- Cons: Fragments the editing UX (other details stay inline on Details); more scaffolding for a
  single field; YAGNI given current scope.

### C: New editable Process attributes (description, public code, dates)
- Pros: Richer Process metadata.
- Cons: Expands the data model and schema; not what the user asked for; out of scope.

## Decision

**Approach A — inline name edit on the Details page, Name only.** User explicitly chose "Name
only" scope and "inline on Details" UI. Additional user decision: **renaming is allowed at any
Process status, including Closed** (intentionally inconsistent with the other mutations, which
are blocked when Closed — but it's the simpler option since `Process.Rename()` has no Closed
guard today, so no domain change is needed). Spec written as **030-edit-process-name**, reviewed
SOUND by `speckit-spex-gates-review-spec` (no critical/important issues). New audit event
`process.renamed`; new `RenameProcessCommand` + `IProcessService.RenameAsync`; success toast
"Nombre del proceso actualizado."; duplicate error reuses "Ya existe un proceso con ese nombre."

## Open Threads

- RowVersion / optimistic-concurrency handling on the rename happy path (constitution Quality
  Gate) — duplicate-name races are covered by the unique index; lost-update on the name field
  itself is the only residual, low risk for an admin-only single field — pin in `/speckit-plan`.
- Closed-Process rename policy — shipped as "allowed at any status." Revisit only if audit
  integrity of historical (Closed) cycle names is later challenged; the `process.renamed` audit
  entry (old → new) is the mitigation.
- Stable `data-testid` hooks for the new inline form so the E2E rewrite has reliable selectors.
