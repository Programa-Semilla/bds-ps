# Review Brief: Review & Funding-Agreement UX Refinements

**Spec:** specs/027-review-funding-ux/spec.md
**Generated:** 2026-05-26

> Reviewer's guide to scope and key decisions. See full spec for details.

---

## Feature Overview

A consolidated set of eight UX/data refinements from a stakeholder walkthrough of the funding-agreement flow. The unifying goal: make submitted-item decision data legible and consistent at every reviewer/applicant touchpoint, give reviewers expected controls, and make forms self-explanatory. Delivered as eight independently testable user stories. No schema change; es-CR throughout; the generated PDF document body stays unchanged (spec 018 preserved).

## Scope Boundaries

- **In scope:** generator name (not GUID) on the funding-agreement page; confirm step before executing/rejecting the signed convenio; richer applicant detail on that page; one consistent detailed decision summary on all five interaction screens; reviewer-settable applicant code on the first review screen; consistent required-field markers on every form; HTML hover tooltips on applicant fields; sidebar regrouped into Inicio/Administración/Proceso.
- **Out of scope:** changing PDF document content; user profile (already shipped); legal-ID check-digit validation; any DB schema change.
- **Why these boundaries:** The decision-summary expansion is a screen concern, not a document concern (stakeholder clarified). The PDF's legal minimalism (spec 018) must not regress. The existing code field and tooltip scaffold are reused rather than rebuilt.

## Critical Decisions

### On-screen decision summary, PDF untouched
- **Choice:** The "ample detail" requirement is satisfied on the five screens; the PDF document body is unchanged.
- **Trade-off:** Reviewers/applicants get full detail interactively, but the signed PDF stays minimal per spec 018.
- **Feedback:** Confirm no consumer expects the expanded breakdown inside the PDF itself.

### Reuse CodigoPersonal for the reviewer-assigned code
- **Choice:** Wire the existing (currently dangling) per-user `CodigoPersonal` field with a write surface on the first review screen; read-only on profile. No new field, no migration.
- **Trade-off:** Per-applicant (per-user), so it is shared across that applicant's applications — not per-application.
- **Feedback:** Confirm per-applicant scope is correct (vs per-application).

### Single shared projection for US4
- **Choice:** One line-summary projection + one partial consumed by all five surfaces.
- **Trade-off:** Touches five screens at once; higher blast radius but guarantees consistency and prevents future drift.

### Menu = zero removals
- **Choice:** The stakeholder's example tree was illustrative; the restructure regroups every current item and drops nothing, preserving role-gating exactly.
- **Feedback:** Confirm the proposed placement of items not in the example (Usuarios, Configuración, Plantillas de impacto, Cotizaciones pendientes).

## Areas of Potential Disagreement

### Required-field markers app-wide
- **Decision:** Standardize required markers on **every** form, including admin/reviewer forms, not just applicant-facing ones.
- **Why this might be controversial:** Broader sweep = more views touched and more E2E selector churn.
- **Alternative view:** Limit to applicant-facing forms.
- **Seeking input on:** Whether the app-wide sweep is worth the extra surface area now.

### Tooltip copy authored by Claude
- **Decision:** Claude authors first-pass es-CR HTML tooltip copy for all applicant fields; stakeholder refines later.
- **Why this might be controversial:** Draft copy may need substantial wording edits to match program voice.
- **Alternative view:** Ship the mechanism with empty copy and wait for stakeholder-provided strings.
- **Seeking input on:** Acceptable to ship draft copy and iterate.

## Naming Decisions

| Item | Name | Context |
|------|------|---------|
| Reviewer-assigned code | CodigoPersonal (reused) | Existing per-user field, now writable on the review screen |
| Sidebar groups | Inicio / Administración / Proceso | Three labeled nav sections |
| Process applicants nav item | Starters | Surfaces the existing applications listing, filterable by Process |
| Consequence copy (execute) | "Esto ejecuta el convenio." | US2 confirm dialog |
| Consequence copy (reject) | "Esto rechaza la carga; el solicitante podrá enviar otra." | US2 confirm dialog |
| Required marker label | "campo obligatorio" | aria-label on the red asterisk |

## Open Questions

- [ ] Per-applicant vs per-application scope for the reviewer code (spec assumes per-applicant).
- [ ] Final placement of non-example sidebar items (spec proposes Usuarios+Configuración → Administración; Plantillas de impacto+Cotizaciones pendientes → Proceso).
- [ ] Whether to ship draft tooltip copy now or wait for stakeholder strings.

## Risk Areas

| Risk | Impact | Mitigation |
|------|--------|------------|
| US4 touches five screens via one shared partial | Med | Define the projection contract first; cover each surface with E2E |
| App-wide required-marker sweep churns many views/E2E selectors | Med | Centralize once; rely on UI-quality-over-selector-stability posture |
| Menu restructure accidentally drops a destination | Med | Before/after per-role destination table; FR-022 explicit |
| HTML tooltip rendering | Low | Copy is curated, not user-supplied — no injection surface |

---
*Share with reviewers before implementation.*
