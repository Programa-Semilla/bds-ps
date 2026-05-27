# Code Review — 027 Review & Funding-Agreement UX Refinements

Spec compliance: **100% (27/27 FRs)** against the resolved spec. Unit 490/490,
Integration 306/306, full Playwright E2E 275 passed / 0 failed / 5 skipped
(personally executed). One tracked deferral on [FR-024](spec.md) (Starters
Process pre-filter), aligned with [plan decisions #1/#2](plan.md).

---

## Code Review Guide (30 minutes)

This section guides a code reviewer through the implementation, focusing on
high-level questions that need human judgment. Compliance scores and the
requirement matrix live in the console report, not here.

**Changed files:** 81 files — Application (1 new projection + 1 DTO + 1 display-name
helper, 2 service edits), Web (3 controller edits, 4 new/edited view models, 5
new/edited shared partials, 1 new JS module, ~15 form views swept, `_Layout` +
`_AuthLayout`), and tests (unit, integration, E2E ReviewFundingUx + 1 Storage
test adaptation). No schema, no PDF document changes.

### Understanding the changes (8 min)

- Start with `src/FundingPlatform.Application/Services/DecisionSummaryProjection.cs` and `DTOs/DecisionSummaryLineDto.cs`: US4 is the connective core — one read-only projection over the already-loaded aggregate, rendered identically on five surfaces via [`_DecisionSummary.cshtml`](../../src/FundingPlatform.Web/Views/Shared/_DecisionSummary.cshtml).
- Then `src/FundingPlatform.Web/Views/Shared/_Layout.cshtml`: the US8 sidebar regroup (data block + render loop) — the largest single-file behavioral change.
- Question: the projection runs once per detail render. On the reviewer and applicant screens it issues one extra `GetByIdWithDetailsAsync` load (the controller's own DTO path doesn't expose the entity). Is that acceptable on these non-hot detail pages, or worth threading the entity through the existing service call?

### Key decisions that need your eyes (12 min)

**Never-a-GUID via a post-processor, not a reader change** (`src/FundingPlatform.Application/Services/GeneratorDisplayName.cs`, [FR-001](spec.md)/[FR-002](spec.md))

`IUserStoreReader.GetDisplayNameAsync` keeps its spec-017 contract (falls back to the userId for the activity feed). US1 wraps the result so a deleted/blank generator becomes `"Usuario no disponible"` instead of a GUID.
- Question: is "Usuario no disponible" the right stable label, or do you want a different es-CR phrasing?

**ApplicantResponse form split into summary + capture** (`src/FundingPlatform.Web/Views/ApplicantResponse/Index.cshtml`, [FR-010](spec.md))

The shared read-only `_DecisionSummary` now carries the rich per-line detail (incl. technical specs); the interactive table below was slimmed to just the accept/reject control per item, preserving the submit-enable JS and E2E selectors.
- Question: is the two-block layout (read-only detail above, decision control below) clearer than the old single combined table for the applicant?

**US5 last-write-wins on `CodigoPersonal`** (`src/FundingPlatform.Web/Controllers/ReviewController.cs` `ApplicantCode`, [FR-013](spec.md))

A reviewer sets the applicant's code with no concurrency token; group-overlap auth mirrors the `Review` GET (spec 016).
- Question: is last-write-wins acceptable for this low-contention scalar, as the [plan](plan.md#complexity-tracking) argues?

**Tooltip copy as HTML in `data-hint`, rendered via innerHTML** (`src/FundingPlatform.Web/wwwroot/js/hint-tooltip.js`, [FR-019](spec.md))

Copy is curated es-CR HTML in `Resources/HintCopy.cs`, Razor-encoded into the attribute, decoded by the browser, injected as `innerHTML`. Safe because the copy is never user-supplied.
- Question: comfortable with the `innerHTML` injection given the copy is provider-authored only?

**Starters → existing applications listing, no Process pre-filter** (`_Layout.cshtml` proceso entries, [FR-024](spec.md))

Per [plan decisions #1/#2](plan.md), Starters links to `/Admin/Reports/Applications`; process-scoped filtering is deferred (no Process filter exists on that report, and the sidebar has no current-process context).
- Question: accept the deferral, or is a Process filter on the applications report in scope now?

### Areas where I'm less certain (5 min)

- `src/FundingPlatform.Web/Views/ApplicantResponse/Index.cshtml`: I slimmed the interactive table to keep the load-bearing selectors (`tr.response-item`, `input.decision-accept/reject`, `.submit-response`) and the submit-enable JS. Worth confirming no other consumer relied on the removed supplier/amount/comment columns.
- `src/FundingPlatform.Web/Resources/HintCopy.cs`: first-pass es-CR copy for the applicant field set — wording is mine and meant for stakeholder refinement.
- US6 sweep coverage: I marked the documented form inventory (research D5) plus the shared `_LegalIdField`/`_QuoteFields` partials. A required field on a form outside that inventory could still lack a marker.

### Deviations and risks (5 min)

- `_Layout.cshtml`: the standalone "Procesos" child was folded into the Proceso section header (which links to `/Admin/Processes`). Destination stays reachable ([FR-022](spec.md)), but a reviewer who expected a separate "Procesos" item should confirm this is the intended UX.
- [FR-024](spec.md) Process pre-filter on Starters is deferred (see decision above). Question: is this deviation acceptable for this PR?
- `tests/.../Storage/SignedFundingAgreementUploadDownloadTests.cs`: adapted to click the US2 confirm modal (it drives the Approve button directly). Question: should that test migrate to the `SigningStagePanelPage.ApprovePending` helper instead of inline button clicks?
