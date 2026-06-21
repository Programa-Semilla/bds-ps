# Review Brief: Funds-Usage Evidence Inbox

**Spec:** specs/041-evidence-inbox/spec.md
**Generated:** 2026-06-19

> Reviewer's guide to scope and key decisions. See full spec for details.

---

## Feature Overview

Spec 036 lets reviewers capture funds-usage evidence (bills, photos, documents) on an application after its agreement is executed, "over time as the funds are spent." But once executed, the application drops off every reviewer list, so the evidence stage is only reachable transiently right after execution — a reviewer who navigates away cannot get back. This feature adds a persistent, group-scoped sidebar inbox of executed applications, and bounds the *editable* evidence window to the application's Process lifetime: while the Process is `Active` the stage behaves as today; once `Closed` the application leaves the inbox and its evidence page becomes read-only (view + download only).

## Scope Boundaries

- **In scope:** A role-gated sidebar inbox listing `AgreementExecuted` applications in `Active` processes (group-scoped); a process-close read-only gate on the existing evidence page (UI + server-side); es-CR copy; preservation of existing access control.
- **Out of scope:** Deleting/archiving evidence on close (data preserved), applicant access, notifications, search/pagination, changes to how processes open/close.
- **Why these boundaries:** Reuse-first — no new lifecycle state, no schema change, no new dependency. The smallest change that closes the navigation gap and enforces the "evidence ends when the process ends" rule.

## Critical Decisions

### "Gone when the process closes" = de-listed + read-only, not deleted
- **Choice:** Closing a Process removes the application from the inbox and freezes its evidence page to read-only; stored evidence is preserved and remains downloadable.
- **Trade-off:** Keeps the historical record (auditability) at the cost of the page not being a pure 404 after close.
- **Feedback:** Confirm read-only-with-download is the desired "frozen" behavior (it was confirmed during brainstorming).

### Live process-status check (reopen restores everything)
- **Choice:** Inbox membership and read-only mode are evaluated at request time, so reopening a `Closed` process restores the application to the inbox and re-enables editing.
- **Trade-off:** Simpler and self-correcting; no snapshot/migration needed.

### Admins are frozen too on a closed process
- **Choice:** The read-only freeze applies uniformly; admins keep their broader visibility only for *which* applications appear (group bypass), not for bypassing the freeze.

## Areas of Potential Disagreement

### Read-only freeze applies to admins
- **Decision:** Admins cannot upload/edit/delete evidence once the process is closed.
- **Why this might be controversial:** Admins often retain override powers; some teams expect an admin to still attach a late document.
- **Alternative view:** Allow admin-only writes after close.
- **Seeking input on:** Is the uniform freeze correct, or should admins keep write access post-close?

### Sidebar entry vs. a queue filter
- **Decision:** A dedicated sidebar entry (user's explicit choice) rather than a new filter on the existing review queue.
- **Why this might be controversial:** Adds a navigation item; a queue filter would reuse more existing UI.
- **Alternative view:** Tab/filter on `/Review`.
- **Seeking input on:** Already decided by the user; flagged only for visibility.

## Naming Decisions

| Item | Name | Context |
|------|------|---------|
| Sidebar entry / page | "Evidencia de uso de fondos" | es-CR; mirrors the spec-036 stage card label |
| Process states driving behavior | `Active` / `Closed` | Existing `ProcessStatus`; no new values |

## Open Questions

- [ ] Should admins retain write access to evidence after the process closes, or be frozen like reviewers? (Spec currently assumes frozen.)
- [ ] Preferred inbox row ordering (e.g., most-recently-executed first)? (Left to implementation.)

## Risk Areas

| Risk | Impact | Mitigation |
|------|--------|------------|
| Read-only enforced only in UI (bypass via crafted POST) | High | FR-007 mandates server-side rejection; SC-003 verifies crafted requests |
| New surface weakens spec-036 access control | High | FR-008 preserves no-disclosure refusals; US3 + SC-004 test out-of-group/applicant on both states |
| Inbox query leaks cross-group applications | Med | NFR-001 enforces group-overlap at the query level, not UI hiding |

---
*Share with reviewers before implementation.*
