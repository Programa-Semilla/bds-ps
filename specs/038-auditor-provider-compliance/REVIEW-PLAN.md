# Review Guide: Auditor Role + Provider Regulatory Compliance Model

**Spec:** [spec.md](spec.md) | **Plan:** [plan.md](plan.md) | **Tasks:** [tasks.md](tasks.md)
**Generated:** 2026-06-17

---

## What This Spec Does

It gives the FundingPlatform a clear authority over supplier (provider) regulatory compliance. The existing
`SupplierAdmin` role becomes an **Auditor**; the four true/false compliance checkboxes become three
enumerated Spanish-language statuses (Hacienda / CCSS / SICOP); the unwanted "electronic invoice" control is
removed; and every regulatory change is recorded with a per-field "last reviewed" stamp so reviewers can see
how current the data is. It also adds a provider warning (informational) and emails auditors when a provider
is created.

**In scope:** role rename (members preserved), enumerated statuses + PME/PYME flag, electronic-invoice
removal, regulatory audit trail + freshness display + a "reviewed — no change" action, provider warnings
(auditor-authored / reviewer-visible / non-blocking), and a new-provider email to auditors.

**Out of scope (and why):** the multi-criteria **recommendation algorithm** + delivery/warranty quote fields
(slice B — needs these statuses first), the **auditor application-workflow stage** with checklists/inbox/PDF
(slice C), **freshness *enforcement*** (the 1-month block) + the **daily Hacienda API sync** (slice D), and any
**in-app notification center** (none exists). This is the deliberately-minimal foundation the other three
slices build on — see [decomposition map](../../seeds/feedback-3/00-decomposition.md).

## Bigger Picture

This is slice **A** of an 8-slice decomposition of feedback round 3 (a 2000-line unified requirements doc).
It is the keystone: slices B/C/D all depend on the role and the compliance/audit fields introduced here. Two
choices were made to keep A self-contained while not blocking later work: freshness is *tracked and displayed*
here but *enforced* in D; and the audit trail reuses the generic `AdminAuditEvent` mechanism rather than a
purpose-built table. Both are revisited in [research.md](research.md) (D6) — D may need a dedicated table
because its automated Hacienda job has no human actor for the `NOT NULL` actor FK.

---

## Spec Review Guide (30 minutes)

### Understanding the approach (8 min)

Read [spec.md Purpose](spec.md#purpose) and [User Story 1](spec.md#user-story-1---auditor-manages-provider-regulatory-compliance-priority-p1),
then [research D1](research.md#d1--role-rename-strategy-rename-the-existing-aspnetroles-row) and
[D6](research.md#d6--audit-trail-extend-generic-adminauditevent). As you read:

- Is **renaming the existing `AspNetRoles` row** (so members carry over) the right migration, or would you
  prefer an explicit new-role-plus-member-copy? ([D1](research.md#d1--role-rename-strategy-rename-the-existing-aspnetroles-row))
- The plan keeps the **filter class names** (`SupplierAdminOnly`/`SupplierAdminDenied`) and the supplier-list
  DTO names while renaming the role string. Is that the right balance of churn vs. clarity, or should those be
  renamed too for coherence? ([T010](tasks.md))
- Storing statuses as `TINYINT` with the verbatim Spanish labels in a display map — does decoupling storage
  from label text sit right, given the client insists the Spanish strings are source-of-truth? ([D4](research.md#d4--compliance-status-persistence-enumtinyint-verbatim-labels-in-a-display-map))

### Key decisions that need your eyes (12 min)

**Send the new-provider email via the Notifications sender, not the invitation direct-send path** ([D11](research.md#d11--new-provider-notification-send-via-the-allowlist-wrapped-notifications-iemailsender))
- The survey found the spec-033 invitation/forgot-password direct-send path is **not** allowlist-wrapped — only
  the Notifications-path `IEmailSender` is. So the plan sends through the Notifications sender to honor the
  non-prod allowlist (FR-023). Question: is that the right fix, or should the direct-send path itself be
  wrapped (a broader change that would also protect invitations)? This is arguably a latent bug in the
  invitation flow — worth a reviewer opinion on whether to fix it here or file it separately.

**Add `RowVersion` to `Supplier`** ([D15](research.md#d15--concurrency), [plan Complexity Tracking](plan.md#complexity-tracking))
- The spec assumed an optimistic-concurrency token that doesn't exist today (`Supplier` has only `UpdatedAt`).
  The plan adds `RowVersion`. Question: acceptable scope for slice A, or defer and accept last-write-wins until
  slice D needs it?

**Reuse `AdminAuditEvent` vs a dedicated provider-audit table** ([D6](research.md#d6--audit-trail-extend-generic-adminauditevent))
- Payload carries `{field, oldValue, newValue, source, kind}` and `TargetId=supplierId`. Question: is the
  generic payload acceptable, or do you want the typed prev/new columns now (knowing D may force the table
  anyway)?

**"Reviewed — no change" disabled until a value exists** ([D9](research.md#d9--oq-reviewed--no-change-before-a-value-is-set))
- Resolves the spec's open question by disabling re-authorize on an unset status. Question: does any workflow
  need to "confirm reviewed = still nothing on file"? If so, this is the wrong call.

### Areas where I'm less certain (5 min)

- **Reviewer review render site** ([T031](tasks.md), [research D13](research.md#d13--review-surface-render-sites-for-warning--freshness)):
  the spec requires warnings + freshness "during application review," but the exact reviewer partial isn't
  fully pinned — the plan defers it to a grep at implementation. If reviewers review applications somewhere I
  haven't identified, that surface could be missed. Worth confirming where reviewers actually see supplier
  quotes.
- **Funding-agreement PDF** ([T012](tasks.md)): the PDF `_SupplierVerificationPage` currently prints the old
  booleans as "Al día"/"Pendiente". I treat repointing it to the new statuses as a forced in-scope fix, but
  the *display semantics* (which statuses read as "compliant" on a legal PDF) may deserve a deliberate mapping
  decision rather than a 1:1 label swap.
- **"Any creation path"** ([spec FR-021](spec.md#requirements), [research D11](research.md#d11--new-provider-notification-send-via-the-allowlist-wrapped-notifications-iemailsender)):
  today the only provider-creation path is the applicant draft-supplier flow. The notifier wires there. If a
  future/admin/import path appears, the trigger must be re-checked — fine for now, but the "regardless of
  source" wording is broader than the current surface.

### Risks and open questions (5 min)

- The role rename spans ~50 sites ([research D1](research.md#d1--role-rename-strategy-rename-the-existing-aspnetroles-row)).
  If any `User.IsInRole("SupplierAdmin")` or `[Authorize]` literal is missed, an auditor silently loses access
  or a screen leaks. Is the grep-sweep task ([T040](tasks.md)) sufficient assurance, or do you want an explicit
  before/after capability table per controller?
- Dropping the four BIT columns on the Azure prod path (which publishes with `--no-drop`) needs deliberate
  handling ([quickstart watch-out](quickstart.md), [data-model migration notes](data-model.md#migration--concurrency-notes)).
  Dev/E2E are greenfield; is the prod-drop sequencing something to spell out now or at deploy time?
- Each provider creation sends one email per auditor synchronously (best-effort). With many auditors, is that
  acceptable, or should it be batched/queued? ([D11](research.md#d11--new-provider-notification-send-via-the-allowlist-wrapped-notifications-iemailsender))

---
*Full context in linked [spec](spec.md), [plan](plan.md), and [research](research.md).*
