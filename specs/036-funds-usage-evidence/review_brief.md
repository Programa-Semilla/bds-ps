# Review Brief: Funds-Usage Evidence Stage

**Spec:** specs/036-funds-usage-evidence/spec.md
**Generated:** 2026-06-16

> Reviewer's guide to scope and key decisions. See full spec for details.

---

## Feature Overview

Adds a post-disbursement stage to the application lifecycle: once an application's funding agreement is executed (funds given), in-scope reviewers get an **"Evidencia de uso de fondos"** surface to upload files (photos, PDFs, Office docs) proving the funds were used correctly. Reviewers can annotate each item with an optional ≤250-char note, download items, and delete them. It is an open, ongoing collection — no new lifecycle state, no completion gate. Reviewer/Admin only, group-scoped; applicants do not see it in this iteration.

## Scope Boundaries

- **In scope:** Upload (multi-file), list with metadata, optional editable note (≤250 chars), download, delete (with confirm), audit of all three mutations, es-CR copy, reviewer+admin group-scoped access, availability gated on the executed-agreement state.
- **Out of scope:** Applicant visibility; any evidence approval/review/scoring; required-evidence gating or "closing" the application; versioning/history; malware scanning beyond type+size checks.
- **Why these boundaries:** Deliver the core compliance need (capture proof) simply; defer anything that adds a workflow or a new state until there's a concrete requirement (YAGNI / Constitution VI).

## Critical Decisions

### Trigger = executed-agreement state
- **Choice:** The stage opens only when the application reaches `AgreementExecuted` (the signed agreement is approved/executed).
- **Trade-off:** Ties "funds given" to the last existing lifecycle state rather than a new "disbursed" concept.
- **Feedback:** Is `AgreementExecuted` the right proxy for "funds disbursed", or is there a real disbursement event that should gate this instead?

### Open collection, no new lifecycle state
- **Choice:** Evidence accrues while the application stays `AgreementExecuted`; no new enum state, no "complete evidence" action.
- **Trade-off:** Simplicity over an explicit "evidence phase complete" signal.
- **Feedback:** Will anyone need to mark evidence collection as finished/closed later?

### Any in-scope reviewer can delete any item
- **Choice:** Deletion is not restricted to the uploader.
- **Trade-off:** Collaborative cleanup vs. tighter ownership control.
- **Feedback:** Acceptable, given the audit trail records who deleted what?

## Areas of Potential Disagreement

### File-type allow-list
- **Decision:** Accept images (jpg/png/webp/heic), PDF, Office (Word/Excel); reject everything else.
- **Why this might be controversial:** The raw request said "all types of files."
- **Alternative view:** Accept truly any type (only enforce the size cap).
- **Seeking input on:** Is the curated allow-list acceptable, or must genuinely-any type be allowed?

### Applicants cannot see evidence
- **Decision:** Reviewer/admin only for now.
- **Why this might be controversial:** Applicants may expect to see/confirm what was recorded against their funding.
- **Alternative view:** Give applicants read-only visibility.
- **Seeking input on:** Confirm applicant visibility stays deferred.

## Naming Decisions

| Item | Name | Context |
|------|------|---------|
| Stage label (es-CR) | "Evidencia de uso de fondos" | The reviewer-facing stage title |
| Domain concept | Funds-Usage Evidence Item | One uploaded file + metadata + optional note |
| Trigger state | AgreementExecuted | Existing lifecycle state that opens the stage |

## Open Questions

- [ ] Confirm `AgreementExecuted` is the correct "funds disbursed" trigger.
- [ ] Confirm the curated file-type allow-list vs. genuinely-any type.
- [ ] Confirm applicant visibility remains deferred.

## Risk Areas

| Risk | Impact | Mitigation |
|------|--------|------------|
| Sensitive evidence exposed to wrong reviewer | High | Group-scoped + reviewer/admin-only access (FR-002/FR-009); no-disclosure refusals |
| Orphaned files or list entries on partial failure | Medium | No DB row without a stored blob; delete removes blob then row (FR-007, edge cases) |
| Unbounded storage growth (no max count) | Low | 20 MiB per-file cap; revisit a per-application quota only if needed |

---
*Share with reviewers before implementation.*
