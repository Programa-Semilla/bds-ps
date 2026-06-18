# Review Brief: Auditor Workflow Stage

**Spec:** specs/040-auditor-workflow-stage/spec.md
**Generated:** 2026-06-18

> Reviewer's guide to scope and key decisions. See full spec for details.

---

## Feature Overview

Inserts a mandatory **Auditor** stage between reviewer completion and the funding agreement reaching the applicant for signature (feedback-3 slice C). The reviewer completes a checklist and hands off to an auditor; the auditor completes their own checklist, approves, **generates the agreement PDF, confirms it is correct, and releases it for signature**. Non-compliance returns the application to the reviewer (never to the applicant) with per-item reasons. PDF generation moves from reviewer to auditor; the downstream signing ceremony is unchanged.

## Scope Boundaries

- **In scope:** Two new states (`PendingAudit`, `ReturnedFromAudit`); reviewer "Send to audit" gate; auditor global inbox + reviewer-equivalent read access; per-stage checklist templates (admin-managed); auditor approve → generate PDF → confirm → release; return-to-reviewer with reasons + email; audit trail of all transitions.
- **Out of scope:** Per-process/group-scoped checklists or inbox; a new audit→applicant route; any change to PDF content, signing ceremony, or `AgreementExecuted`; regulatory-freshness **blocking** (that's slice D — this slice only displays freshness).
- **Why these boundaries:** Keeps the slice independently shippable on top of slice A; defers blocking/automation to slice D; reuses existing PDF/outbox/signing seams.

## Critical Decisions

### Insertion point wraps the generate-agreement step
- **Choice:** The auditor gate sits *after* `ResponseFinalized` (post applicant per-item response and any appeal), replacing the old "reviewer/admin generates agreement" trigger.
- **Trade-off:** The master doc imagined a simpler "reviewer ready → PDF → sign" chain; reality has the applicant response loop in between. The PDF needs accepted-item data, which only exists post-`ResponseFinalized`, forcing this placement.
- **Feedback:** Confirm this is the intended gate location.

### §28.9 resolved as per-stage templates
- **Choice:** `appliesToStage = reviewer | auditor | both`; one active template per stage globally; seed one default.
- **Trade-off:** Slightly more model than a single hard-coded shared list, but future-proofs (split later) with no migration; rejected per-process scoping as speculative.

### Return path is "lean"
- **Choice:** A non-compliant audit returns to a new `ReturnedFromAudit` state where the reviewer reworks and re-sends to audit. Re-engaging the applicant uses existing reopen/appeal machinery — no new audit→applicant route.
- **Trade-off:** Satisfies §11.6's hard requirements without duplicating appeal machinery.

## Areas of Potential Disagreement

### Global auditor inbox (not group-scoped)
- **Decision:** Auditors see every application in `PendingAudit` regardless of reviewer group.
- **Why this might be controversial:** Reviewers *are* group-scoped (spec 016); an asymmetry.
- **Alternative view:** Large deployments with many auditors partitioned by program might want scoping.
- **Seeking input on:** Is a global auditor inbox acceptable for the foreseeable client scale?

### Admin can perform auditor actions
- **Decision:** Auditor-stage actions are available to Auditor OR Admin; reviewers lose direct agreement generation.
- **Why this might be controversial:** Some orgs want a hard separation of duties (auditor-only).
- **Alternative view:** Auditor-only, admins excluded unless they hold the role.
- **Seeking input on:** Confirm the admin-override posture is desired.

### Degenerate pass when a stage has no active checklist
- **Decision:** No applicable active template → zero required items → gate immediately passable.
- **Why this might be controversial:** Could let an application slip through without verification if misconfigured.
- **Alternative view:** Block the gate when no checklist is configured.
- **Seeking input on:** Accept the seeded-default mitigation, or require a non-empty checklist?

## Naming Decisions

| Item | Name | Context |
|------|------|---------|
| New state — in audit | `PendingAudit` | Application awaiting auditor (master §11.4 candidate) |
| New state — bounced | `ReturnedFromAudit` | Returned to reviewer with findings |
| Checklist stage tag | `reviewer` / `auditor` / `both` | §22.9 `appliesToStage` |
| Reviewer action | "Send to audit" | Replaces former "Generate agreement" |
| Auditor confirmation | "PDF is correct" | §19.3 extra confirmation gate |

## Open Questions

- [ ] Seeded default template's stage applicability — recommend `both` (plan-phase pin).
- [ ] Generating-actor recorded on the agreement now that the auditor generates it (plan-phase detail).

## Risk Areas

| Risk | Impact | Mitigation |
|------|--------|------------|
| Removing reviewer's direct generate-agreement path ripples through existing E2E tests | Med | Treat as cross-cutting; rewrite affected funding-agreement/signing E2E to route through audit |
| New states must slot cleanly into the existing state machine without breaking appeal/signing | High | States are strictly between `ResponseFinalized` and signing; no appeal interaction; domain-gated transitions |
| Concurrent auditor actions on one application | Low | Optimistic concurrency per existing aggregate convention |

---
*Share with reviewers before implementation.*
