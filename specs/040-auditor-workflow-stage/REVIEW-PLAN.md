# Review Guide: Auditor Workflow Stage

**Spec:** [spec.md](spec.md) | **Plan:** [plan.md](plan.md) | **Tasks:** [tasks.md](tasks.md)
**Generated:** 2026-06-18

---

## What This Spec Does

Today, once an application is fully reviewed and the applicant has responded, a reviewer or admin generates the funding-agreement PDF and it goes straight to the applicant to sign. This feature (feedback-3 slice C) inserts a mandatory **auditor** between those two moments: the reviewer completes a checklist and hands the application to an auditor; the auditor completes their own checklist, approves, **generates and confirms the PDF**, and only then releases it for signature — or sends it back to the reviewer with reasons. It turns the Auditor role (shipped in slice A) into a real workflow actor and moves PDF generation from reviewer to auditor.

**In scope:** Two new application states ([`PendingAudit`, `ReturnedFromAudit`](data-model.md#1-enums-domain)); a reviewer "Send to audit" gate; a **group-scoped** auditor inbox + reviewer-equivalent read access; admin-managed per-stage checklist templates; the auditor approve → generate → confirm → release path; a return-to-reviewer path with per-item reasons; two notifications (one new, one re-pointed); full audit trail.

**Out of scope:** Per-process/per-fund checklists; a new "audit → applicant" route (the reviewer uses existing reopen/appeal paths); any change to the PDF content, the signing ceremony, or `AgreementExecuted`; regulatory-freshness **blocking** (that's slice D — this slice only *displays* slice-A freshness). These boundaries are where reviewer feedback is most valuable.

## Bigger Picture

This is the third feedback-3 slice. Slice A ([spec 038](../038-auditor-provider-compliance/spec.md), PR #69) created the Auditor role + provider compliance model; slice B ([spec 039](../039-supplier-recommendation/spec.md), PR #70) rewrote the recommendation algorithm. Slice C consumes A's role and compliance surface and slots a workflow stage into the existing `Application` state machine. Slice D (regulatory-freshness blocking + Hacienda API) will later build on the same auditor surface. The whole round is mapped in [`seeds/feedback-3/00-decomposition.md`](../../seeds/feedback-3/00-decomposition.md).

The single most consequential design choice is how two new states fit a state machine whose signing ceremony lives *entirely inside* `ResponseFinalized` — see the decision review below.

---

## Spec Review Guide (30 minutes)

### Understanding the approach (8 min)

Read the [Workflow Context diagram](spec.md#workflow-context) and [research D1](research.md#d1--where-the-auditor-gate-slots-in-the-keystone). The auditor stage wraps the *existing* generate-agreement step rather than replacing the whole tail of the workflow.

- The agreement PDF is built from the applicant's accepted items, which only exist at `ResponseFinalized`. Does it follow that the auditor stage *must* sit there (and could not sit earlier, e.g. right after the reviewer finalizes)?
- "Send to audit" is offered only when `State==ResponseFinalized && no agreement exists yet`. Is that a robust way to distinguish the pre-audit phase from the post-release signing phase, given both reuse `ResponseFinalized`? (See [D1's "disambiguating" note](research.md#d1--where-the-auditor-gate-slots-in-the-keystone).)

### Key decisions that need your eyes (12 min)

**Release returns to `ResponseFinalized` instead of adding a third state** ([research D1](research.md#d1--where-the-auditor-gate-slots-in-the-keystone))

To keep the signing ceremony literally unchanged (its applicant-upload gate and `ExecuteAgreement` both require `ResponseFinalized`), `ReleaseForSignature` transitions `PendingAudit → ResponseFinalized` with the PDF already generated.
- Question: is a "backward-looking" edge (`PendingAudit → ResponseFinalized`) acceptable, or would an explicit `AwaitingSignature` state be clearer despite touching the signing-ceremony guards? The spec deliberately chose two states over three — does that trade legibility for blast-radius in the right direction?

**Auditors are group-scoped, symmetric with reviewers** ([FR-006](spec.md#functional-requirements)/[FR-017](spec.md#functional-requirements), [research D7](research.md#d7--auditor-inbox--group-scoped-mirrors-the-reviewer-queue-updated-2026-06-18))

This was changed late from a global inbox at stakeholder request. An auditor sees only applications whose applicant shares one of their groups; they must be assigned to groups via the admin user form.
- Question: every auditor must be assigned to the right group(s) or their inbox is empty — is that operational assumption safe for the client? (Same failure mode reviewers already have.)

**Checklist scope = per-stage templates** ([FR-001](spec.md#functional-requirements)/[FR-002](spec.md#functional-requirements), [research D4](research.md#d4--checklist-scope--per-stage-templates-resolves-289))

Resolves open decision §28.9: `AppliesToStage = Reviewer | Auditor | Both`, one active per stage, global, seed one `Both` default.
- Question: when both a stage-specific *and* a `Both` template are active, the plan says stage-specific wins and the service enforces "at most one active per effective stage." Is that precedence rule intuitive, or should activating a stage-specific template auto-deactivate a conflicting `Both`?

**A non-empty checklist is not required to pass a gate** ([Edge Cases](spec.md#edge-cases), [data-model §7](data-model.md#7-validation-rules-from-spec-frs))

If no active template applies to a stage, the gate has zero required items and passes immediately (degenerate pass), mitigated only by the seeded default.
- Question: should a misconfiguration (no active checklist) **block** the gate instead, to prevent an application slipping through unverified?

**New auditor notification on send-to-audit** ([FR-018](spec.md#functional-requirements), [research D10](research.md#d10--notifications-one-new-event--one-re-point))

"Receive notifications the same way reviewers do" was interpreted as a new `SentToAuditAuditor` event to group-scoped auditors, requiring a new Auditor recipient bucket.
- Question: is adding a new notification + recipient bucket the right reading of that line, or did the stakeholder only mean "scoped the same way" (no new email)? This is the interpretation I'm least sure of — see below.

### Areas where I'm less certain (5 min)

- [FR-018](spec.md#functional-requirements): I read "auditors receive notifications the same way reviewers do" as *adding* a send-to-audit notification ([research D10](research.md#d10--notifications-one-new-event--one-re-point)). It could instead mean only that recipient resolution is group-scoped, with no new email. If the latter, drop the `SentToAuditAuditor` event and tasks T036/T037.
- [research D8](research.md#d8--auditor-read-access--reuse-the-reviewer-review-projection): the auditor read surface reuses `ReviewService.GetApplicationForReviewAsync`, which auto-transitions `Submitted → UnderReview`. That branch never fires for a `PendingAudit` app, so reuse is read-safe — but if a thin no-auto-transition projection is cleaner, that's a task-time call I left open.
- [research D6](research.md#d6--checklist-response-model--frozen-snapshot-fr-003): I modeled checklist responses as *overwrite-current-cycle* (with `VersionHistory` carrying cross-cycle audit) rather than append-per-cycle. For a `PendingAudit ⇄ ReturnedFromAudit` loop, is losing prior-cycle response *rows* (while keeping the transition history) acceptable for audit purposes?

### Risks and open questions (5 min)

- The reviewer's direct "Generate agreement" action is removed ([FR-005](spec.md#functional-requirements)). Existing `FundingAgreement` / `Signing` / `GenerateAgreementQueue` E2E assume reviewer-generated agreements ([tasks T053–T056](tasks.md#phase-7-polish--cross-cutting-concerns)). Is rewiring those in the Polish phase the right sequencing, or should the seeder change land in Foundational so stories don't fight stale fixtures?
- [FR-010](spec.md#functional-requirements) edge case: regenerating the PDF clears the auditor's confirmation. If an auditor regenerates after releasing… they can't — release left `PendingAudit`. Is there any path where a regenerate-after-release is reachable that the plan misses?
- Admin user-form change (T030) is the only edit to an existing high-traffic admin surface. Could showing the group selector for the Auditor role disturb the reviewer-role rendering it currently shares?

---
*Full context in linked [spec](spec.md), [plan](plan.md), and [tasks](tasks.md).*
