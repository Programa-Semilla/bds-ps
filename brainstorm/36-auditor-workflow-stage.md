# Brainstorm: Auditor Workflow Stage (feedback-3 slice C)

**Date:** 2026-06-18
**Status:** spec-created
**Spec:** specs/040-auditor-workflow-stage/

## Problem Framing

Feedback-3 slice C turns the Auditor role (created in shipped slice A, `038`) into a workflow actor. The client wants a mandatory audit between reviewer completion and the funding agreement reaching the applicant: the reviewer completes a checklist and sends the application to an auditor; the auditor completes an audit checklist, approves, generates the PDF, confirms it, and releases for signature — or returns it to the reviewer with reasons. PDF generation moves from reviewer to auditor. Master sections: §11, §12, §18, §19, §22.9–22.11, §23.1/23.2, §25.2/25.4, §28.9.

The crux discovered during exploration: the master doc's mental model (§11.1 "reviewer ready → generate PDF → applicant signs") is simpler than the real codebase, where the agreement PDF is built from the applicant's per-item Accept/Reject response and can only be generated at `ResponseFinalized` (after the response and any appeal). That logically pins the auditor stage to wrap the existing generate-agreement gate.

## Approaches Considered

### A: Wrap the generate-agreement gate (chosen)
- Insert two new states (`PendingAudit`, `ReturnedFromAudit`) between `ResponseFinalized` and the signing ceremony. Reviewer's "Generate agreement" becomes "Send to audit"; auditor owns generation.
- Pros: Logically forced (PDF needs accepted items); minimal disruption to the response/appeal loop; signing ceremony untouched.
- Cons: Removes the reviewer's direct generate path → cross-cutting E2E ripple.

### B: Audit before the applicant response (rejected)
- Place audit right after reviewer Finalize (`Resolved`), before the per-item response.
- Cons: Impossible — the PDF the auditor must generate/confirm depends on accepted-item data that doesn't exist yet.

### Checklist model (§28.9)
- (a) single shared list, (b) separate per-stage templates, (c) one template with role-specific sections.
- Chose **(b)** — `appliesToStage = reviewer | auditor | both`. A `both` template covers the client's "same checklist for both" out of the box; future split needs no migration. (a) is a degenerate case of (b); (c) adds complexity with no requirement.

### Return-path scope
- Chose **lean**: return → new `ReturnedFromAudit` state → reviewer reworks + re-sends to audit. Applicant re-engagement uses existing reopen/appeal machinery (no new audit→applicant route).

### Scoping
- Chose **global** for both the auditor inbox and checklist templates (no per-process/group scoping) — matches §18.1 wording and avoids wiring group membership into the new role; nothing requires scoping yet.

## Decision

Build slice C as a two-state auditor gate wrapping the agreement-generation step, with per-stage checklist templates, global auditor inbox, lean return path, admin-OR-auditor permissions, and re-pointed "ready to sign" + new return-to-reviewer notifications. Signing ceremony and PDF content unchanged. Spec created at `specs/040-auditor-workflow-stage/` and passed the soundness gate (SOUND, no critical/important issues).

Decisions locked with the user:
1. Insertion point wraps generate-agreement; signing ceremony stays as-is. ✓
2. §28.9 → per-stage templates (b). ✓
3. Return path lean; new dedicated `ReturnedFromAudit` state. ✓
4. Global auditor inbox + global single-active-per-stage checklists. ✓
5. Include return-to-reviewer email; re-point existing applicant "ready to sign"; auditor actions available to Auditor OR Admin (reviewer loses direct generate). ✓

## Open Threads

- Seeded default checklist template's `appliesToStage` (recommend `both`) — pin during planning.
- Which user is recorded as the generating actor on the agreement now that the auditor generates it — plan-phase data-model detail.
- Cross-cutting E2E ripple: existing funding-agreement/signing tests assume reviewer-generated agreements; they will need rewiring to route through the audit stage.
