# Review Brief: Admin — Edit Process Name

**Spec:** specs/030-edit-process-name/spec.md
**Generated:** 2026-06-10

> Reviewer's guide to scope and key decisions. See full spec for details.

---

## Feature Overview

Admins currently cannot change a Process's name after creation — it's set once and is immutable
everywhere in the UI, even though every other Process detail (Fund, stage windows, Plantilla,
Groups, close) is editable on the Process detail page. This feature adds an inline edit
affordance for the **Name** on `/Admin/Processes/{id}`, so a typo or relabel can be fixed in
place without recreating the Process (which would lose its Groups, Plantilla snapshot, and
history). The change is audited and the new name propagates to the detail header, breadcrumb,
and the Processes list.

## Scope Boundaries

- **In scope:** Inline rename of the Process **Name** on the detail page; validation
  (required, ≤120 chars, unique); es-CR copy; an audit entry; works for Active and Closed
  Processes.
- **Out of scope:** Editing any other Process field (already editable); a dedicated `/Edit`
  page; inline rename from the list rows; bulk rename.
- **Why these boundaries:** Name was the only Process detail with no edit affordance. Keeping
  the feature to one field, reusing existing patterns, satisfies the user's request with
  minimal surface area (constitution Principle VI).

## Critical Decisions

### Inline on the Details page (not a dedicated Edit page)
- **Choice:** Add an inline Name card to the existing Details page, matching the Fund/stage-window
  inline forms.
- **Trade-off:** Less "conventional CRUD" than an Edit page, but consistent with how all other
  Process details are already edited; no new navigation.
- **Feedback:** Confirm you're happy keeping all Process editing consolidated on the detail page.

### Rename allowed at any status, including Closed
- **Choice:** The Name affordance renders for Closed Processes too; no "closed" guard on rename.
- **Trade-off:** Intentionally *inconsistent* with Fund/windows/Plantilla/Groups, which are all
  blocked once a Process is Closed. This was an explicit user decision.
- **Feedback:** Confirm renaming a historical (Closed) cycle is acceptable.

## Areas of Potential Disagreement

### Rename on a Closed Process
- **Decision:** Allowed.
- **Why this might be controversial:** Closed Processes are historical records; some would argue
  their identifying name should be frozen for audit integrity.
- **Alternative view:** Block rename when Closed, matching every other Process mutation.
- **Seeking input on:** Whether the audit entry (which records old → new name) is sufficient to
  preserve traceability, making the rename safe even on closed cycles.

## Naming Decisions

| Item | Name | Context |
|------|------|---------|
| Audit event | `process.renamed` | New `AdminAuditEvent.ProcessRenamed`, follows the `process.*` convention |
| Success toast (es-CR) | "Nombre del proceso actualizado." | Shown on successful rename |
| Duplicate error (es-CR) | "Ya existe un proceso con ese nombre." | Reused verbatim from Process create |

## Open Questions

- [ ] Confirm the Closed-Process rename policy (see Disagreement Areas).
- [ ] Confirm success-toast wording.

## Risk Areas

| Risk | Impact | Mitigation |
|------|--------|------------|
| Lost-update on Name under concurrent admin edits | Low | `Process.RowVersion` optimistic concurrency already exists; decide handling in plan |
| Duplicate name under concurrent rename | Low | Existing `UX_Processes_Name` unique index is the authoritative gate |
| E2E selector churn on the Details page | Low | Add stable `data-testid` hooks for the new form |

---
*Share with reviewers before implementation.*
