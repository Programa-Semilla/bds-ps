# Review Guide: PDF Template Lift — Branded Funding Agreement

**Spec:** [spec.md](spec.md) | **Plan:** [plan.md](plan.md) | **Tasks:** [tasks.md](tasks.md) | **Research:** [research.md](research.md) | **Data model:** [data-model.md](data-model.md) | **Contracts:** [contracts/README.md](contracts/README.md)
**Generated:** 2026-05-08

---

## What This Spec Does

Replaces the current generic "Convenio de Financiamiento" PDF with a branded six-section "Informe de evaluación de solicitudes de desembolso" that pixel-matches a canonical seed template. Adds two enabling domain inputs — applicant-side `Application.CompanyName` (cover-page `Empresa solicitante`) and reviewer-side `Item.LineCode` (the `Variable` column on every PDF table) — and removes the dead `FundingAgreement:Funder:*` configuration left over from the prior generic layout.

**In scope:** branded chrome (header logo + partner-strip footer on every page, A4 portrait, teal/cream/gold palette, Fraunces+Inter typography); six-page sequence (cover → intro → recursos → resultados → proveedores → declaración jurada); reviewer `LineCode` capture with per-Application uniqueness; applicant `CompanyName` capture; deletion of `FunderOptions`, the funder DTO block, the spec-005 placeholder banner, and the legacy partials.

**Out of scope:** admin UI for swapping logos (file-on-disk replacement suffices); a `Tract` entity; per-partner footer logos (composite stays for v1); broader applicant/reviewer form revisions; legacy-data backfill (no production users yet); visual-diff automation.

## Bigger Picture

Closes a credibility loop: spec 005 stood up the funding-agreement endpoint, spec 006 added digital signing, spec 015 added multi-currency notes — all on top of a placeholder layout that carried a visible "MARCADOR DE POSICIÓN" banner. Spec 018 retires that banner and treats the seed PDF as canonical legal copy ([research.md CLARIFICATION-1](research.md#clarification-1--sworn-declaration-legal-canonicity)).

Renderer engine (Syncfusion Blink) is reused unchanged ([R-001](research.md#r-001--header--footer-rendering-on-every-page-syncfusion-blink)); only the HTML/CSS input and `@page` margins change. Header/footer repetition rides on CSS `position: fixed` rather than `BlinkConverterSettings.PdfHeader/Footer` — Blink-supported but non-obvious. Repeating `<thead>` rides on `display: table-header-group` ([R-003](research.md#r-003--page-break-inside-tables-header-band-repeats)).

Schema change is dacpac-only ([R-006](research.md#r-006--schema-migration-with-no-production-data)): `dbo.Applications` gains `CompanyName NVARCHAR(200) NOT NULL`; `dbo.Items` gains `LineCode NVARCHAR(16) NULL` plus a filtered unique index. No EF migrations, no production-data shim. Constitution II (rich domain) and Constitution IV (schema-first) are load-bearing.

---

## Spec Review Guide (30 minutes)

> Each section points to specific spec / plan / task locations and frames the review as questions you can answer by reading the linked anchor.

### Understanding the approach (8 min)

Read the [Summary in plan.md](plan.md#summary), [User Story 1 in spec.md](spec.md#user-story-1---branded-restructured-funding-agreement-pdf-priority-p1), and [research.md R-001 / R-002](research.md#r-001--header--footer-rendering-on-every-page-syncfusion-blink). As you read:

- Is the "rewrite the renderer chain in one shot, no version flag" choice in [FR-017](spec.md#requirements-mandatory) and [Constitution VI in plan.md](plan.md#constitution-check) the right risk posture, given the existing PDF is already in production-shaped use by spec 006 / 015 tests? See [T055](tasks.md#phase-6-polish--cross-cutting-concerns) for how it expects to fix downstream test fallout.
- Is "treat the seed PDF as canonical Legal copy" (per [research.md CLARIFICATION-1](research.md#clarification-1--sworn-declaration-legal-canonicity)) the right default given the lone `[NEEDS CLARIFICATION]` in [Open Clarifications](spec.md#open-clarifications) is still unanswered? The fallback is to keep the placeholder banner, which would defeat [SC-006](spec.md#measurable-outcomes).
- Does the [data-model.md Item section](data-model.md#item-modified) make sense — specifically the journey from "NOT NULL with empty-string-as-unassigned" to "NULL with filtered unique index"? The two variants both appear in the same document; the second is the adopted decision but the first variant is still printed at line 65–90 and could mislead an implementer reading top-down.

### Key decisions that need your eyes (12 min)

**Renderer rewrite with no parallel renderer or feature flag** ([FR-017](spec.md#requirements-mandatory), [plan.md Constitution VI](plan.md#constitution-check))

The plan deletes the legacy partials in [T016](tasks.md#cleanup-of-legacy-generic-template-artefacts-fr-019024) and rewrites `Document.cshtml` in [T034](tasks.md#implementation-for-user-story-1) — there is no toggle to fall back to the old layout if the new one regresses spec-006 signing or spec-015 conversion notes.
- Question for reviewer: is the [T055](tasks.md#phase-6-polish--cross-cutting-concerns) "fix the downstream tests" remediation acceptable, or do we want a flag for one release to derisk the spec-006 signature-anchor change?

**Reviewer form: LineCode required at decision time, not at item creation** ([R-008](research.md#r-008--reviewer-form-per-item-linecode-input-ux), [Contract 2](contracts/README.md#contract-2--post-reviewidreviewitem-reviewer--capture-line-code-with-decision))

The plan threads LineCode through the same POST as `Approve / Reject / RequestMoreInfo` and lets the aggregate root (`Application.AssignLineCodeToItem`) enforce non-blank + uniqueness in one transactional boundary.
- Question: [Contract 2 line 45](contracts/README.md#contract-2--post-reviewidreviewitem-reviewer--capture-line-code-with-decision) lists LineCode as required only when `Decision ∈ {Approve, Reject}` (not for `RequestMoreInfo`), but [R-008](research.md#r-008--reviewer-form-per-item-linecode-input-ux) says "RequestMoreInfo also threads LineCode" and [FR-012](spec.md#requirements-mandatory) says LineCode is required "for each item before the per-item review decision can be persisted" without carving out RequestMoreInfo. Which is canonical? Implementer needs one answer.

**Applicant form: CompanyName captured at Create, not at Submit** ([R-007](research.md#r-007--application-form-where-is-companyname-captured))

Because `CompanyName` is non-nullable from day one, the Create form gains its first real input. Existing test fixtures will all need to thread `CompanyName` through `new Application(applicantId, ...)` ([T050](tasks.md#implementation-for-user-story-3)).
- Question: is the breakage radius in test fixtures (and any existing seed scripts) understood? T050 says "search `tests/` for `new Application(applicantId)`" but doesn't enumerate hits — is there a non-test seeder somewhere (Aspire fixture) that also needs the update?

**Schema choice: NULL `LineCode` with filtered unique index vs NOT NULL with empty-string sentinel** ([data-model.md Item section](data-model.md#item-modified), [R-006](research.md#r-006--schema-migration-with-no-production-data))

Adopted: NULL + `WHERE LineCode IS NOT NULL` filtered unique index. The same document still prints the rejected NOT-NULL variant at the top of the section.
- Question: should [data-model.md](data-model.md#item-modified) be tightened so the rejected variant is removed (or moved into an "Alternatives considered" subsection) before implementers read it? An implementer who skims top-down today could pick the wrong column nullability.

**Header/footer rendering via CSS `position: fixed` rather than Syncfusion `PdfPageTemplateElement`** ([R-001](research.md#r-001--header--footer-rendering-on-every-page-syncfusion-blink))

Less code in the renderer, more reliance on Blink CSS support.
- Question: [Outstanding Risk #2 in research.md](research.md#outstanding-risks) flags this exact concern ("Blink CSS gaps for `position: fixed` headers across page breaks on long tables") with a smoke-test mitigation. Is a 50-row fixture covered by any of the listed tasks ([T021](tasks.md#tests-for-user-story-1) / [T022](tasks.md#tests-for-user-story-1) / [T036](tasks.md#implementation-for-user-story-1))? T036 is perf only; the long-table-header-repeat case isn't called out as its own assertion.

### Areas where I'm less certain (5 min)

- [data-model.md Item section](data-model.md#item-modified) presents two contradictory schema variants in the same document; the second is the adopted one but the first is not struck through. An attentive reviewer will notice; a hurried implementer might not.
- [Contract 2](contracts/README.md#contract-2--post-reviewidreviewitem-reviewer--capture-line-code-with-decision) and [R-008](research.md#r-008--reviewer-form-per-item-linecode-input-ux) disagree on whether `RequestMoreInfo` requires a `LineCode`. [FR-012](spec.md#requirements-mandatory) is silent on the carve-out. The implementer needs one source of truth before [T010](tasks.md#application-command-shape-changes) ships.
- [T024](tasks.md#implementation-for-user-story-1) introduces a new error code `LineCodeMissingOnApprovedItems` that isn't enumerated in [Contract 2](contracts/README.md#contract-2--post-reviewidreviewitem-reviewer--capture-line-code-with-decision) or [Contract 3](contracts/README.md#contract-3--get-fundingagreementapplicationidgenerate--pdf-funder-operator). It's defence-in-depth so probably fine, but the contracts page doesn't say so.
- [T012](tasks.md#view-model-rewrite-fr-019023) says "decide in T019" whether `FundingAgreementItemRowDto` survives. Live "decide later" branches in a task list invite drift; an implementer might land T012 with a half-updated DTO that T019 then deletes (rework).
- [T035](tasks.md#implementation-for-user-story-1) hardcodes mm→pt conversion (`20mm = 56.69pt`) in prose. The Syncfusion `BlinkConverterSettings.Margin` units may actually be points already — worth confirming against the current renderer's existing margin code before the implementer copies the constants.

### Risks and open questions (5 min)

- The lone [NEEDS CLARIFICATION on sworn-declaration legal canonicity](spec.md#open-clarifications) is unresolved; [research.md CLARIFICATION-1](research.md#clarification-1--sworn-declaration-legal-canonicity) chose the spec's documented default (canonical). If Legal later marks the seed as draft, [FR-024](spec.md#requirements-mandatory) and [SC-006](spec.md#measurable-outcomes) flip — is anyone chasing Legal in parallel, or is "default to canonical" the team's accepted risk?
- [SC-001](spec.md#measurable-outcomes) is a manual side-by-side check governed by ±5pt tolerance. [T052](tasks.md#phase-6-polish--cross-cutting-concerns) is the only task that exercises it. Is one developer eyeballing the seed enough, or does this want a second reviewer pass for sign-off?
- [SC-009](spec.md#measurable-outcomes) (3-second p95 PDF gen) is measured by a perf script in [T036](tasks.md#implementation-for-user-story-1) / [T051](tasks.md#phase-6-polish--cross-cutting-concerns) but explicitly **not** a CI gate. If the renderer regresses after merge (e.g. someone adds a 5-table partial), what catches it before the next funder runs into it?
- [Outstanding Risk #4](research.md#outstanding-risks) — existing E2E tests in spec 005 / 006 / 015 reference removed funder fields, the placeholder banner, or deleted partials. [T055](tasks.md#phase-6-polish--cross-cutting-concerns) is the catch-all "make it green" task. Is the scope of that breakage understood (or is "go green" effectively a black box)?
- The [FR-018](spec.md#requirements-mandatory) "swap-one-file" ergonomics for the footer composite is fine for v1, but the [Out of Scope](spec.md#out-of-scope) section flags that per-partner edits need the composite re-cut externally. Is there a plan for when a partner logo changes (e.g. SBD rebrands), or is that "deal with it then"?
- [T043](tasks.md#implementation-for-user-story-2) accepts a "placeholder LineCode like `T1-{itemIndex}`" in tests "that don't care about codes". Risk: a placeholder could leak into a fixture-rendered PDF assertion in spec-005/006 tests and make the new-PDF assertions accidentally pass for the wrong reason. Is there a test-only marker convention that would be safer (e.g. `TEST-{itemId}`)?

---

*Full context in linked [spec](spec.md), [plan](plan.md), [research](research.md), [data-model](data-model.md), and [contracts](contracts/README.md).*
