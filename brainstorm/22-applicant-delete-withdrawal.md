# Brainstorm: Applicant-initiated application delete / withdrawal

**Date:** 2026-05-21
**Status:** spec-created
**Spec:** specs/021-feedback-session-may13/ (folded in as User Story 9 + FR-035–FR-041 + SC-017/SC-018)

## Problem Framing

An applicant who started an Application has no way to remove it. Drafts they abandoned and submissions they no longer wish to pursue linger on the applicant dashboard and (once submitted) clutter the reviewer queue. Only admins can soft-delete today (`AdminApplicationsController.SoftDelete`). Goal: give applicants a self-service exit.

Existing infra discovered up front:
- `Application.SoftDelete()` + `DeletedAt` + `IsDeleted` already exist (`Application.cs:77-159`).
- `ApplicationQueryFilter.ExcludeDeleted` already drops soft-deleted rows from every dashboard surface (spec 021 FR-021).
- spec 021-email-notifications outbox + `NotificationRecipientResolver` already in production.
- **No per-application "assigned reviewer"** — reviewer access is spec-016 group overlap; `APPLICATION_SUBMITTED_REVIEWER` notifies the whole stage-group reviewer pool.

## Decisions (from collaborative Q&A)

1. **Scope of states:** Draft → delete; Submitted/UnderReview → withdraw. (Not "any pre-resolution state"; Resolved+ stays locked.)
2. **Withdraw semantics:** reuse the existing soft-delete (`DeletedAt`). No new `Withdrawn` domain state. Vanishes from applicant dashboard + reviewer queue.
3. **Reviewer notification:** yes — but reconciled against reality. There is no single assignee, so the recipient set is the stage-group reviewer pool (the `APPLICATION_SUBMITTED_REVIEWER` set). Fires **only when `UnderReview`**; plain `Submitted` withdrawal is silent; empty pool is a no-op. New event `APPLICATION_WITHDRAWN_BY_APPLICANT` + es-CR Razor template.
4. **Placement / UX:** affordance on dashboard `Index` + Application `Details`, state- and ownership-gated; explicit confirmation; labels *"Eliminar borrador"* / *"Retirar solicitud"*; server re-checks state + ownership (defense in depth).
5. **Home:** folded into spec 021-feedback-session-may13 (current branch), not a new numbered spec.

## Approaches Considered

### A: Reuse soft-delete end-to-end (CHOSEN)
- Pros: zero schema change; rides production-proven admin soft-delete path, `ExcludeDeleted`, and the spec-021 outbox; smallest change surface.
- Cons: no first-class record distinguishing applicant-withdrawn from admin-deleted (mitigated — withdrawal email is the reviewer-facing signal).

### B: New `Withdrawn` terminal domain state
- Pros: cleanest audit trail; reviewer/admin keep visibility of withdrawn items.
- Cons: enum value + reviewer-surface handling + query changes; rejected for YAGNI given soft-delete already does the job.

### C: Soft-delete + dedicated AdminAuditEvent record
- Pros: audit trail without a new state.
- Cons: extra plumbing for marginal benefit at this stage; parked.

## Key reconciliation (review gate caught it)

The spec-review gate flagged that "assigned reviewer" / "skip if unassigned" had no basis in the code (group-overlap model, pool-based notification). Surfaced to the user; resolved to the `UnderReview`-only stage-group-pool notification. Spec wording (US9 narrative, scenarios 2–3, FR-039, FR-040, SC-017/018, edge cases, assumption) updated to match.

## Open Threads

- OQ-11: confirm with stakeholders that `UnderReview`-only is the right notification trigger (vs. notifying on any `Submitted` withdrawal, or never).
- Idempotency-key shape for `APPLICATION_WITHDRAWN_BY_APPLICANT` (must distinguish from other reviewer-bucket events for the same Application) — pin in `/speckit-plan`.
- Whether withdrawal should leave any applicant-visible "Retirada" trace on their own dashboard (currently it just vanishes like a soft-delete) — parked.
