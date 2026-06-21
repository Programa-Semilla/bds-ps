# Brainstorm: Funds-Usage Evidence Inbox (reviewer navigation + process-close read-only)

**Date:** 2026-06-19
**Status:** spec-created
**Spec:** specs/041-evidence-inbox/

## Problem Framing

Spec 036 added the post-disbursement "Evidencia de uso de fondos" stage where reviewers upload evidence on an executed application "over time as the funds are spent." But the stage is only reachable via a conditional link on the funding-agreement panel, and once an agreement is executed the application drops off every reviewer list (review queue = Submitted/UnderReview/ReturnedFromAudit/Resolved; signing inbox = Pending uploads only). A reviewer who navigated away had no way back — they saw the option once and couldn't find it again. The brainstorm started from that user report.

## Approaches Considered

### A: Dedicated sidebar entry (chosen)
- Pros: Persistent, obvious, always-available entry point; matches user's explicit preference; reuses the group-scoped reviewer-query seam; isolates the new surface.
- Cons: Adds a sidebar item; a second list surface to maintain.

### B: New filter/tab on the existing review queue
- Pros: Reuses existing queue UI and projection machinery; no new nav item.
- Cons: Executed apps conceptually differ from the active worklist; user preferred a separate entry.

### C: Keep executed apps in the signing inbox
- Pros: Minimal.
- Cons: Semantically wrong (signing inbox is for pending signatures); rejected.

## Decision

Chosen **A** (separate sidebar entry). Two user decisions shaped scope:
1. **Sidebar entry**, not a queue filter.
2. **"When the process closes the results should be gone too"** → resolved to: the application is de-listed from the inbox when its governing Process is `Closed`, and the evidence page becomes **read-only** (view + download preserved; upload/edit/delete removed from UI and rejected server-side). Evidence data is preserved, not deleted. Reopening the process restores everything (live status check). Read-only freeze applies to admins too.

Reuse-first: no new `ApplicationState`, no schema change, no new managed dependency. Builds on the executed-agreement state, the existing `ProcessStatus` (Active/Closed), the spec-016 reviewer-scope rule, and the spec-036 evidence stage. Extends spec 036's access model by adding the process-close read-only gate.

Spec reviewed SOUND (specs/041-evidence-inbox/REVIEW-SPEC.md). 4 user stories (P1 reviewer-returns, P2 process-close-freeze, P2 access-control-preserved), 10 FRs + 2 NFRs, 5 SCs.

## Open Threads

- Should admins retain write access to evidence after the process closes, or be frozen like reviewers? (Spec assumes frozen; flagged in review_brief.)
- Inbox row ordering (e.g., most-recently-executed first) — deferred to implementation.
- Related session bug (not part of this spec): the reviewer queue did not surface `ReturnedFromAudit` applications; fixed in `ReviewerQueueProjection` earlier this session (separate, uncommitted on this branch at brainstorm time).
