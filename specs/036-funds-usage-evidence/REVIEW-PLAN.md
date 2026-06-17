# Review Guide: Funds-Usage Evidence Stage

**Spec:** [spec.md](spec.md) | **Plan:** [plan.md](plan.md) | **Tasks:** [tasks.md](tasks.md)
**Generated:** 2026-06-16

---

## What This Spec Does

When an application's funding agreement has been executed (funds disbursed), this adds a reviewer-only stage to upload files that prove the money was used correctly — photos of receipts, PDFs, spreadsheets — each with an optional short note. Reviewers can list, download, annotate, and delete these items. It's the first thing that happens *after* the existing lifecycle ends, and for now it's invisible to applicants.

**In scope:** Upload (multi-file), a list with metadata, an optional editable ≤250-char note, download, delete-with-confirm, an audit trail of all three mutations, es-CR copy, and group-scoped reviewer/admin access — all gated on the [`AgreementExecuted`](spec.md#assumptions) state.

**Out of scope (and worth a reviewer's eye):** applicant visibility, any approve/reject/score *workflow* on the evidence, "closing" the application, versioning, and malware scanning. See [Out of Scope](spec.md#out-of-scope). The deferrals are deliberate (YAGNI), but they're exactly the kind of boundary where a stakeholder might say "actually, we need X now."

## Bigger Picture

The application state machine today is `Draft → Submitted → UnderReview → Resolved → ResponseFinalized → AgreementExecuted` ([ApplicationState](../../src/FundingPlatform.Domain/Enums/ApplicationState.cs)). `AgreementExecuted` has been a terminal state — nothing happens after it. This feature gives that terminal state a purpose without extending the enum: evidence is modeled as an *open collection* hanging off the application, not a new lifecycle phase ([research D1/D2](research.md)).

The whole feature is deliberately built from parts that already exist and are battle-tested: object storage (spec 014), reviewer group-scoping (spec 016), the toast/confirm system (spec 024), and the audit-event sink (spec 016). That's why there are no new dependencies and no new state. The risk profile is therefore less "will this work" and more "did we pick the right seams and the right boundary."

---

## Spec Review Guide (30 minutes)

### Understanding the approach (8 min)

Read [User Story 1](spec.md#user-story-1---collect-funds-usage-evidence-on-an-executed-application-priority-p1) and the [Assumptions](spec.md#assumptions). As you read, consider:

- The whole feature hinges on equating "funds given to the person" with the `AgreementExecuted` state. Is that the right proxy? In your domain, is there a real *disbursement* moment (a bank transfer, a signed receipt) that happens later than agreement execution and would be a more honest trigger? ([research D1](research.md#d1--availability-trigger--agreementexecuted))
- The stage is an *open collection* with no "done" — reviewers add and remove evidence forever. Is there ever a point where evidence collection should be considered complete/locked? If yes, the [no-new-state decision](research.md#d2--fundsusageevidence-as-its-own-aggregate-creation-gated-by-a-domain-factory) would need revisiting before, not after, implementation.

### Key decisions that need your eyes (12 min)

**Curated file-type allow-list vs. "all types"** ([FR-004](spec.md#functional-requirements), [research D3](research.md#d3--file-type-policy-curated-allow-list-ext--content-type--magic-byte-family))

The raw request said "all types of files." The spec narrowed that to images + PDF + Office docs, validated by extension + content-type + magic bytes.
- Question: Is the curated list acceptable, or will reviewers legitimately need to attach things outside it (CAD files, `.zip` bundles, `.csv`, video)? Loosening later is easy; the question is whether the v1 list blocks real evidence.

**Any in-scope reviewer can delete anyone's evidence** ([FR-007](spec.md#functional-requirements))

Deletion isn't restricted to the uploader; the audit trail is the safety net.
- Question: Is an audit row enough accountability for a destructive action on compliance evidence, or do you want delete restricted to the uploader/admin?

**Applicants can't see the evidence** ([Out of Scope](spec.md#out-of-scope))

- Question: Applicants are the people whose funds these are. Is reviewer-only visibility going to generate "why can't I see what you recorded against me" friction, or is that genuinely a later concern?

**FK is `ON DELETE NO ACTION`, not CASCADE** ([data-model.md](data-model.md#table-ddl-dbofundsusageevidencesql))

This diverges from the `SignedUploads` precedent (which cascades). The rationale is that applications are soft-deleted, never hard-deleted, so cascade is unnecessary and NO ACTION avoids the multi-cascade publish failures that bit specs 029/035.
- Question: Does that reasoning hold — is there truly no path that hard-deletes an `Application` row?

### Areas where I'm less certain (5 min)

- [research D6](research.md#d6--audit-via-iadminauditwriter--new-adminauditevent-action-keys): `AdminAuditEventWriter` routes audit rows to a target by action-key *prefix*. I assumed adding a `funds_evidence.` prefix means extending that routing switch, but I didn't fully trace the writer — if it routes differently, the audit tasks ([T009](tasks.md)) need adjustment.
- [research D7](research.md#d7--surfacing-the-stage-on-the-per-application-reviewer-surface): I chose to hang the stage link off the per-application funding-agreement surface rather than the queue-level `_ReviewTabs`. I'm confident that's the right *concept* (evidence is per-application), but the exact partial to edit is left to implementation — a reviewer who knows the review UI might have a strong opinion on where the link should live.
- The multi-file upload UX ([contracts/ui-and-routes.md](contracts/ui-and-routes.md#routes)): I left "one POST with several files" vs. "one file per POST" open. It doesn't change the data model, but it changes the form and the E2E.

### Risks and open questions (5 min)

- If a reviewer uploads a phone photo that's a `.heic` with an odd content-type, will the [type policy](research.md#d3--file-type-policy-curated-allow-list-ext--content-type--magic-byte-family) accept it? HEIC magic-byte sniffing (the `ftyp` box) is the fiddliest of the families — worth a real-device test.
- The E2E suite needs an application in `AgreementExecuted`, which requires driving the whole signing ceremony in setup ([research D8](research.md#d8--e2e-seed-must-reach-agreementexecuted)). If no reusable helper exists, [T011](tasks.md) adds a dev-seam fast-forward. Is a Development-only fast-forward seam acceptable in this codebase (it already has `SeedUser`/`AssignAllGroups` seams), or should the E2E go the long way through the real ceremony?
- No per-application cap on evidence count or total size (only 20 MiB/file). Is unbounded accumulation a storage-cost concern worth a soft limit now? ([spec edge cases](spec.md#edge-cases))

---
*Full context in linked [spec](spec.md), [plan](plan.md), and [tasks](tasks.md).*
