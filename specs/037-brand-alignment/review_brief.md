# Review Brief: Programa Semilla Official Brand Alignment (037)

**Spec:** specs/037-brand-alignment/spec.md
**Generated:** 2026-06-17

> Reviewer's guide to scope and key decisions. See full spec for details.

---

## Feature Overview

A visual-only facelift that re-anchors the FundingPlatform web UI to the **official** Programa
Semilla brand book — exact palette (`#008A9E` teal / `#42AFA8` light teal / `#F9A61C` orange /
`#FFC729` yellow), real logo assets (horizontal / vertical / icon), and an official combined
partner-logo footer image. It supersedes the PDF-sampled approximations and placeholder logos that
spec 019 shipped, realizing spec-019 OQ-001 (designer override once the brand book pins exact
values). Beyond a palette swap it adds structural refinements: a dark teal sidebar, de-zebra'd
tables, standardized teal-CTA page headers, filter cards with a clear-filters action, and a kebab
actions menu. No backend logic, business rules, routes, permissions, data, or schema change.

## Scope Boundaries

- **In scope:** Global design tokens; real brand/logo assets; shared chrome (dark sidebar, white
  topbar, footer image); standardized page headers; filter cards + "Limpiar filtros"; de-zebra'd
  teal tables; kebab actions column; typography; favicon; full surface re-sweep (applicant +
  reviewer + admin + auth); and a narrow PDF brand-asset re-tint to the official teal.
- **Out of scope:** Backend logic, business rules, DB schema/models, permissions/roles,
  route/action renames, adding/removing functionality, PDF generation logic/layout/body content,
  localization layer, Tabler upgrade, new managed deps, public marketing surface.
- **Why these boundaries:** The client constraint is "visual facelift using the official brand
  identity" — chrome and styling only. The system is already token-driven, so a re-tint + asset
  swap covers most of it; the few structural changes (sidebar, tables, actions, footer) are
  explicitly requested.

## Critical Decisions

### Full re-sweep (all roles), not admin-only
- **Choice:** Re-skin every applicant + reviewer + admin + auth surface, like spec 019.
- **Trade-off:** Largest scope and most E2E/POM rewrites — but the shared dark sidebar + tokens make
  a partial scope artificial.
- **Feedback:** Confirm the appetite for the full sweep (vs. shipping admin first).

### PDF brand assets re-tinted to `#008A9E` now
- **Choice:** Re-tint PDF logo/partner chrome to the official teal so UI and PDF reconverge.
- **Trade-off:** A deliberate, documented exception to spec 019 FR-039 (which froze PDF assets);
  generation pipeline/layout/content stay untouched.
- **Feedback:** Confirm this narrow exception is acceptable (the alternative was accepting a small
  screen-vs-PDF teal delta).

### Kebab actions column (Option A)
- **Choice:** "Editar" visible + "⋯" menu for Reenviar invitación / Restablecer / Inhabilitar.
- **Trade-off:** Cleaner rows, but changes E2E selectors for the relocated actions (POM rewrites).
- **Feedback:** Confirm Option A over keeping all buttons inline (Option B).

### Official combined footer image (partner-set change)
- **Choice:** Single `Fooder-general.png` replaces the 5 individual sponsor SVGs.
- **Trade-off:** Partner set changes — drops "10 años", adds "De la mano con su PYME".
- **Feedback:** Confirmed acceptable during brainstorming (OQ-B).

## Areas of Potential Disagreement

### Dark sidebar for ALL roles, including applicants
- **Decision:** The dark `#12343B` sidebar applies to every role.
- **Why this might be controversial:** Applicants currently see a lighter, friendlier shell; a dark
  navy sidebar is more "enterprise admin" in feel.
- **Alternative view:** Keep a lighter sidebar for applicant-facing surfaces.
- **Seeking input on:** Confirmed "all roles" during brainstorming (OQ-A) — flag if you want to
  revisit for the applicant experience.

### Page background shifts to off-white `#F6F8FA`
- **Decision:** Page bg moves from pure white to `#F6F8FA`; cards stay white.
- **Why this might be controversial:** Spec 019 deliberately moved to clean white; this re-introduces
  a faint tint for surface separation.
- **Alternative view:** Keep page bg pure white and rely on borders/shadow for separation.
- **Seeking input on:** Whether the off-white page token reads well across dense admin tables.

## Naming Decisions

| Item | Name | Context |
|------|------|---------|
| New supporting teal token | `--color-primary-light` (`#42AFA8`) | badges, hover, secondary highlights |
| New status/attention token | `--color-accent-orange` (`#F9A61C`) | "pending/attention" indicators |
| New dark-sidebar tokens | `--color-sidebar-bg` / `--color-sidebar-hover` | `#12343B` / `#174A53` |
| Removed token | `--color-table-zebra` | cream zebra eliminated |
| New filter affordance | "Limpiar filtros" | clear-filters action beside "Aplicar" |

## Open Questions

- [ ] OQ-001: Raster-as-provided vs. request vector original if the auth-hero vertical logo renders soft.
- [ ] OQ-002: Exact sidebar white-container treatment (full-bleed pill vs. off-white card).
- [ ] OQ-003: Is `#F9A61C` orange wired to an existing status, or held in reserve?
- [ ] OQ-004: Should the PDF partner-strip also adopt the new official partner set, or teal re-tint only?

## Risk Areas

| Risk | Impact | Mitigation |
|------|--------|------------|
| E2E selector churn from kebab + de-zebra + sidebar restructure | Med | Budgeted POM rewrites; per-surface brand assertions; filtered-E2E-green delivery bar |
| Dark-sidebar text/contrast regressions | Med | Light text token `#D9E6E8`; axe AA on ≥5 surfaces incl. sidebar |
| Yellow misused as semantic color | Low | Decorative-only contract + grep/lint gate (carried from spec 019) |
| PDF re-tint accidentally alters layout/content | Med | Asset-color-only change; fixture PDF diff (SC-013) |
| Raster logo softness at large sizes | Low | NFR-005 sizing + OQ-001 fallback to vector request |

---
*Share with reviewers before implementation.*
