# Review Guide: Funds-Usage Evidence Inbox

**Spec:** [spec.md](spec.md) | **Plan:** [plan.md](plan.md) | **Tasks:** [tasks.md](tasks.md)
**Generated:** 2026-06-19

---

## What This Spec Does

A reviewer finishes the signing ceremony for an application, funds get disbursed, and weeks later bills and photos arrive that need to be filed against that application. Spec 036 built the place to file them — but once an agreement is executed, the application disappears from every reviewer list, so the reviewer who navigates away can never find it again. This spec gives reviewers/admins a persistent **"Evidencia de uso de fondos"** sidebar inbox of executed applications, and freezes the evidence stage to read-only once the application's governing Process is closed.

**In scope:** A role-gated sidebar inbox listing `AgreementExecuted` applications whose Process is `Active` (group-scoped); a process-close read-only gate on the existing evidence page (view + download stay, writes blocked in UI and server-side); es-CR copy; preservation of spec-036 access control.

**Out of scope:** Deleting/archiving evidence on close (data is preserved, just frozen); any applicant access; notifications; search/pagination; changing how/when a process opens or closes. See [Out of Scope](spec.md#out-of-scope).

## Bigger Picture

This is a direct follow-on to [spec 036 (funds-usage evidence)](../036-funds-usage-evidence/spec.md), reusing its evidence stage, storage, and access rules, and to [spec 016 (user groups)](../016-user-groups/spec.md) for the group-overlap scoping. It deliberately rides on the existing `ProcessStatus` (`Active`/`Closed`) lifecycle rather than inventing a new "evidence window" concept — which is why it needs no new `ApplicationState`, schema, or dependency. The notable contrast is with the just-completed [spec 040](../040-auditor-workflow-stage/spec.md), which added new TINYINT state/tables and hit EF conversion gotchas; this feature was scoped specifically to avoid all of that. The one architectural question it raises for the future (not addressed here) is whether the process-close guard — which predates spec 040 and ignores `PendingAudit`/`ReturnedFromAudit` — should be revisited.

---

## Spec Review Guide (30 minutes)

> Focus your time on the judgment calls. Each section points to a specific place and frames the review as questions.

### Understanding the approach (8 min)

Read the [Summary](spec.md#summary) and [User Story 1](spec.md#user-story-1---reviewer-returns-to-an-executed-application-to-add-evidence-priority-p1), then the [Assumptions](spec.md#assumptions). As you read:

- The fix is "a separate sidebar inbox," chosen over a filter on the existing review queue. Is a dedicated nav item the right call, or would a tab on `/Review` be more discoverable? (The user explicitly asked for a separate entry — see [research D1](research.md#d1--where-the-inbox-lives).)
- The inbox is bounded to `Active` processes. Does tying the *editable window* to the Process lifecycle match how your team actually decides "evidence collection is over"? Could an application's evidence legitimately need updating after its process closes?

### Key decisions that need your eyes (12 min)

**"Gone when closed" = de-listed + read-only, not deleted** ([FR-006](spec.md#functional-requirements), [FR-010](spec.md#functional-requirements))

The user said results "should be gone" when the process closes; this was interpreted as *removed from navigation and frozen to read-only*, with the files preserved and still downloadable.
- Question: Is preserving + freezing the right reading of "gone," or did the user mean the evidence should become inaccessible entirely? (Confirmed read-only-with-download during brainstorming, but it's the load-bearing interpretation — see [data-model read-only derivation](data-model.md#read-only-derivation-web-layer).)

**The read-only freeze applies to admins too** ([Assumptions](spec.md#assumptions), [research D5](research.md#d5--admin-behavior-under-a-closed-process))

Admins keep broader *visibility* (they see which apps list regardless of group) but cannot upload/edit/delete once the process is closed.
- Question: Should an admin retain write access after close — e.g. to attach a late document or correct a mistake — or is a uniform freeze correct? This is the single decision most likely to need a stakeholder ruling.

**Server-side rejection, not just hidden buttons** ([FR-007](spec.md#functional-requirements), [research D4](research.md#d4--read-only-enforcement-mechanism))

Closed-process mutations are rejected at the controller, covering crafted POSTs, with the UI controls additionally hidden.
- Question: The chosen rejection shape is "redirect to the page with an es-CR toast" (to match the existing UX) rather than a 403. Is that the behavior you want for a blocked write, or would you prefer a hard refusal status?

### Areas where I'm less certain (5 min)

- [FR-003](spec.md#functional-requirements) (row identity): I chose application number + applicant + fund/process names and most-recently-executed ordering ([research D8](research.md#d8--inbox-row-identity--ordering)). Whether that's the right column set / sort for how reviewers scan the list is a UX judgment I can't fully verify from the spec.
- [Out of Scope](spec.md#out-of-scope) — the pre-existing evidence link on the funding-agreement panel (`FundingAgreement/Details`) will, after close, simply land on the now-read-only page. I treated that as acceptable (the page-level mode governs behavior regardless of entry point), but I didn't add a task to change or remove that link — confirm that's fine.
- [tasks.md T018](tasks.md): the access-control "task" is largely verification + a code-comment safeguard on check-ordering, since the underlying gates already exist in spec 036. If you'd expect net-new authorization code here, that expectation and the plan diverge.

### Risks and open questions (5 min)

- If `AgreementExecuted` ever *did* block process closure, the entire read-only scenario would be unreachable. I verified it does **not** ([research D6](research.md#d6--process-close-does-not-block-on-executed-apps-feature-reachability)) — but that guard lives in `ProcessService` and could change. Is it worth a regression test pinning "executed apps don't block close"?
- The inbox is a capped list of 200 with no pagination ([data-model query predicate](data-model.md#query-predicate-executed--active-process--in-scope)). For a long-running program with many executed applications in active processes, could a single group exceed 200 and silently hide work? Is a cap acceptable for v1, or does this need pagination sooner than "out of scope" implies?
- The process-close guard ignores the spec-040 `PendingAudit`/`ReturnedFromAudit` states (noted in [research D6](research.md#d6--process-close-does-not-block-on-executed-apps-feature-reachability)). Out of scope here, but does it warrant its own follow-up?

---
*Full context in linked [spec](spec.md), [plan](plan.md), and [tasks](tasks.md).*
