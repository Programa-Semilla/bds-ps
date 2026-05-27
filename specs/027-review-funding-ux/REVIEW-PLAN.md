# Review Guide: Review & Funding-Agreement UX Refinements

**Spec:** [spec.md](spec.md) | **Plan:** [plan.md](plan.md) | **Tasks:** [tasks.md](tasks.md)
**Generated:** 2026-05-26

---

## What This Spec Does

A stakeholder walkthrough of the funding-agreement flow produced eight UX/data fixes for the reviewer and applicant journeys. The throughline: make the per-line decision data (what was approved/rejected, by which supplier, for how much, and why) legible and *identical* wherever either party looks at it, give reviewers a couple of controls they expected but didn't have, and make forms self-explanatory.

**In scope:** generator name instead of a GUID; a confirmation step before executing/rejecting a signed convenio; a richer applicant block on the funding-agreement page; one shared decision-summary rendered on five screens; a reviewer-settable applicant code; app-wide required-field markers; HTML hover tooltips on applicant fields; a regrouped sidebar.

**Out of scope (and worth a reviewer's eye):** the generated **PDF document body is not touched** ([Out of Scope](spec.md#out-of-scope), [FR-009](spec.md#requirements)) — all the "ample detail" lives on screen, deliberately preserving spec 018's minimal legal document. No DB schema change ([FR-027](spec.md#requirements)). The user profile is treated as already-shipped.

## Bigger Picture

This is the second consolidated "feedback session" spec (after `021-feedback-session-may13`), and it leans hard on prior work rather than building new infrastructure: spec 024's confirm dialog (US2), spec 021's dangling `CodigoPersonal` column and inert `[Hint]`/`_HintTooltip` scaffold (US5/US7), spec 015's currency-conversion note (US4), spec 016's group-overlap authorization (US5), spec 026's identification formatting (US3). The interesting tension is between this spec's "show more, everywhere" intent and spec 018's deliberate decision to *strip* the PDF down — resolved by keeping the expansion on-screen only. Worth confirming that resolution still satisfies the original Banca/Contraloría reporting motivation that kicked this off.

The US4 shared projection is the piece most likely to outlive this spec: once five surfaces read from one `IDecisionSummaryProjection`, future line-data changes have a single home.

---

## Spec Review Guide (30 minutes)

### Understanding the approach (8 min)

Read [US4](spec.md#user-story-4---consistent-detailed-decision-summary-across-all-five-interaction-screens-priority-p1) and the [decision-summary contract](contracts/decision-summary.md). The core bet is that one read-only partial fed by one projection can serve the reviewer's screen, the applicant's screen, and the funding-agreement page across three lifecycle states.

- The reviewer's review screen is an *interactive capture* surface (radios, supplier dropdown, AI comparison). The plan renders the shared read-only summary *alongside* that, not instead of it ([research D3](research.md#d3--us4-shared-decision-summary-projection-core)). Does showing the same data twice on that one screen read well, or should the reviewer screen be exempt from the shared block?
- The projection was deliberately kept *lean* — no AI comparison, scores, or impact parameters — on YAGNI grounds. Is that the right cut, or do reviewers expect the comparison to travel with the summary?

### Key decisions that need your eyes (12 min)

**On-screen only, PDF untouched** ([FR-009](spec.md#requirements), [SC-009](spec.md#success-criteria))
The "ample detail" requirement is satisfied on screens; the signed PDF stays minimal. Question: does any downstream consumer (an auditor, Contraloría) actually need the breakdown *inside* the signed document, or is on-screen + the existing PDF sufficient?

**Reuse `CodigoPersonal`, per-applicant** ([US5](spec.md#user-story-5---reviewer-assigned-applicant-code-on-the-first-review-screen-priority-p2), [research D4](research.md#d4--us5-write-codigopersonal-from-the-review-screen))
The reviewer code is the existing per-user field, shared across that applicant's applications, written via `UserManager`. Question: is per-applicant (not per-application) the right grain — could two applications from the same applicant ever need different codes?

**Required markers app-wide** ([US6](spec.md#user-story-6---consistent-required-field-markers-on-every-form-priority-p2))
The sweep touches ~20 forms including admin/reviewer ones, via a new `_RequiredMark` partial. Question: is the app-wide blast radius (and its E2E selector churn) worth doing now versus applicant-only?

**Sidebar: zero removals, duplication deferred** ([US8](spec.md#user-story-8---restructure-the-left-sidebar-into-grouped-sections-priority-p2), [sidebar-structure.md](contracts/sidebar-structure.md))
Every current item is regrouped, nothing dropped. But the stakeholder's example put "Reportes" and "Plantillas" under *both* Administración and Proceso — and those process-scoped surfaces don't exist. The plan defers them (places each existing surface once). **See the certainty note below — this is the one place the spec and plan currently disagree.**

### Areas where I'm less certain (5 min)

- **[FR-024](spec.md#requirements) vs the plan.** FR-024 says Reportes and Plantillas MUST appear under *both* groups (admin-wide vs process-scoped). The confirmed plan decision *defers* the process-scoped variants because no such routes exist ([plan decision 2](plan.md#plan-review-decisions-confirmed-by-stakeholder-2026-05-26), [sidebar-structure.md open decision 2](contracts/sidebar-structure.md)). As written these conflict. My recommendation is to amend FR-024 to match the deferral (Starters is the new Proceso surface; admin-wide Reportes/Plantillas stay under Administración), but a reviewer should confirm that deferring the process-scoped surfaces is acceptable rather than in scope now.
- **[Starters route](contracts/sidebar-structure.md) ([T035](tasks.md)).** The plan deep-links to the Reports "Applications" tab with a `processId` filter. I'm not certain that tab is URL-addressable today; T035 allows adding minimal routing. If a reviewer knows that surface, confirm the cleanest entry point.
- **US2 reject-comment × confirm ([T008](tasks.md)).** Whether the confirm modal intercepts before the browser validates the mandatory reject comment is unverified; the plan keeps server-side enforcement as the backstop. A reviewer familiar with `confirm-dialog.js` ordering can confirm whether the client gate is also needed.

### Risks and open questions (5 min)

- If the reviewer review screen ([US4](spec.md#user-story-4---consistent-detailed-decision-summary-across-all-five-interaction-screens-priority-p1)) shows both the capture UI and the read-only summary, is that clarifying or cluttering?
- With US4 routed through one partial on five surfaces, does the [five-screen E2E parity test (T017)](tasks.md) adequately guard against a future change silently diverging one surface?
- Does authoring first-pass tooltip copy by AI ([US7](spec.md#user-story-7---html-tooltips-on-applicant-fields-priority-p2)) risk shipping voice-off copy that the stakeholder must then rewrite — and is that acceptable as an interim?

## Prior Review Feedback

Not applicable — first-time spec for this feature.

---
*Full context in linked [spec](spec.md), [plan](plan.md), and [tasks](tasks.md).*
