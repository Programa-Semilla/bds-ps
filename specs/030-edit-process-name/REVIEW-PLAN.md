# Review Guide: Admin — Edit Process Name

**Spec:** [spec.md](spec.md) | **Plan:** [plan.md](plan.md) | **Tasks:** [tasks.md](tasks.md)
**Generated:** 2026-06-10

---

## What This Spec Does

Lets an administrator change a Process's **name** after it's been created, directly on the
Process detail page (`/Admin/Processes/{id}`). Today the name is fixed at creation and can't be
edited anywhere in the UI — even though every other Process detail (Fund, stage windows,
Plantilla, Groups) already can be. The only current "fix" for a typo is to recreate the Process,
which throws away its Groups, Plantilla snapshot, and history.

**In scope:** Inline rename of the Process name; required / ≤120-char / catalog-unique
validation; an audit entry; es-CR copy; rename works for both Active and Closed Processes.

**Out of scope:** Any other Process field (already editable); a dedicated `/Edit` page (rejected
in favor of inline, see [plan Summary](plan.md#summary)); inline rename from the Processes list;
bulk rename.

## Bigger Picture

This is a small follow-on to **029-fund-entity**, which established the `Fund → Process → Group →
Application` hierarchy and added the Fund-reassignment affordance on the Process detail page. This
spec deliberately reuses that exact seam (`ReassignFundAsync`/`ChangeFund`) one more time for the
name — so the interesting review question is less "is the design right" (it mirrors shipped code)
and more "are the two intentional judgment calls correct" (see below). Nothing depends on this
feature; it's a self-contained data-quality fix.

---

## Spec Review Guide (30 minutes)

### Understanding the approach (6 min)

Skim [spec.md User Story 1](spec.md#user-story-1---correct-or-relabel-a-process-name-priority-p1)
and [plan.md Summary](plan.md#summary). The whole feature is one field, reusing a shipped pattern.
As you read, consider:

- Is "inline on the detail page" the right call versus a conventional Edit page? The detail page
  already hosts every other Process edit, so inline keeps editing in one place — but it does mean
  the page keeps accreting small forms. Acceptable, or is the page getting crowded?
- The spec frames recreate-the-Process as the painful status quo. Does that match how admins
  actually work around the gap today?

### Key decisions that need your eyes (14 min)

**Rename is allowed on a Closed Process** ([FR-002](spec.md#functional-requirements), rationale in
[implementation-notes Decision 2](implementation-notes.md#decision-rename-allowed-at-any-status-including-closed))

This is the one place the feature is *intentionally inconsistent* with the rest of the system:
Fund reassignment, stage-window overrides, Plantilla, and Groups are all blocked once a Process is
Closed, but rename is not. The safeguard is the `process.renamed` audit row (old → new name).
- Question: Closed Processes are historical cycles. Is letting their identifying name change
  acceptable given the audit trail, or should a Closed cycle's name be frozen for reporting
  integrity? (This was an explicit product decision, but it's the highest-value thing to confirm.)

**Optimistic-concurrency posture** ([research.md R-1](research.md#r-1--optimistic-concurrency-on-the-rename-path-resolved--the-only-open-question))

The plan does *not* round-trip a RowVersion through the form. It relies on `Process.RowVersion`
(already a SQL rowversion token) guarding the in-request load→save, plus the `UX_Processes_Name`
unique index for duplicate races — matching the sibling `ChangeFund` action exactly.
- Question: For an admin-only single field, is "last write wins across two separate page loads"
  acceptable, or do you want a true stale-edit rejection (hidden RowVersion field)? The plan
  argues YAGNI; do you agree?

**Duplicate-name handling via the unique index, not a pre-check**
([research.md R-3](research.md#r-3--controller-error-mapping-parity-with-create-resolved))

Like the create path, there's no `AnyAsync(name == ...)` pre-check; a duplicate surfaces as
`DbUpdateException` mapped to the existing es-CR string. Consistent and race-safe.
- Question: Comfortable that the duplicate UX (inline error after submit) is good enough, versus
  live/async name-availability feedback? (Live feedback is not in scope.)

### Areas where I'm less certain (5 min)

- [tasks.md T009](tasks.md#implementation-for-user-story-1): the validation-error re-render must
  rebuild the full `AdminProcessDetailsViewModel` (Fund options, Plantilla snapshot, Groups,
  stage windows) exactly as the `Details` GET does, or the page renders half-empty on an invalid
  submit. I flagged it in the task, but it's the most likely place implementation goes subtly
  wrong — worth a careful look in code review.
- [FR-006 / SC-005 no-op](spec.md#functional-requirements): the service decides "unchanged" by
  comparing the trimmed new name to the old name and skipping the audit write. The domain
  `Process.Rename` *also* no-ops internally, so there are two no-op gates; I want to be sure the
  service-level check (not just EF "no changes") is what suppresses the audit row, so the test in
  [T005](tasks.md#tests-for-user-story-1-write-first--must-fail-before-t008t010-land-) asserts it
  directly.

### Risks and open questions (5 min)

- If `AdminAuditEventWriter`'s target-derivation does **not** key purely off the `process.` prefix
  (e.g. it switches on specific constants), then [T002](tasks.md#phase-2-foundational-blocking-prerequisites)
  adding `process.renamed` could land the new event under the wrong target type. T002 says to
  verify this — is a verify-step enough, or should the reviewer confirm the derivation logic
  directly?
- E2E selector stability: the new `data-testid` hooks
  ([T006](tasks.md#tests-for-user-story-1-write-first--must-fail-before-t008t010-land-),
  [T010](tasks.md#implementation-for-user-story-1)) are the contract between the view and the
  Playwright suite. Are the names (`admin-process-rename-form/-input/-submit`) consistent with the
  repo's existing `admin-process-*` testid convention? (They follow the Fund card's style.)
- Delivery bar: per [CLAUDE.md], the feature isn't "done" until the **full** E2E suite is green
  ([T012](tasks.md#phase-4-polish--cross-cutting-concerns)), not just the new tests. Is that
  scheduled into the work, or a surprise at the end?

---
*Full context in linked [spec](spec.md), [plan](plan.md), and [tasks](tasks.md).*
