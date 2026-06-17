# Brainstorm: Official Brand Alignment (facelift-2)

**Date:** 2026-06-17
**Status:** spec-created
**Spec:** specs/037-brand-alignment/

## Problem Framing

The client delivered the **official Programa Semilla brand book** (exact palette image + real logo
files + official partner-strip footer) under `seeds/facelift-2/`. The shipped UI (spec 019) used
**approximations sampled from the funding-agreement PDF** — teal `#1FA0A0`, yellow `#F2C014` — and
**placeholder geometric SVG** mark/wordmark. Spec 019 explicitly anticipated this in **OQ-001**:
"if the Programa Semilla brand book pins a different value, designer override at the sign-off gate."
This session is that override, plus a set of structural refinements the client requested in the seed
+ guideline (`seed-brainstorm-facelift.md`, `facelift-guideline.md`).

Key grounding finding: the platform is **already token-driven** (`tokens.css` is the sole raw-hex
file + a Tabler `--tblr-*` bridge), so most of the work is **remapping ~15 CSS custom properties +
swapping brand asset files**, with a few component-structure changes layered on (dark sidebar,
de-zebra tables, kebab actions, official footer image).

## Approaches Considered

### A: Full re-sweep re-tint + structural refinements (CHOSEN)
- Re-skin every applicant + reviewer + admin + auth surface, like spec 019.
- Remap tokens to the official palette; swap placeholder logos for real assets; add dark sidebar,
  de-zebra tables, standardized teal-CTA page headers, filter cards + "Limpiar filtros", kebab
  actions column, official footer image; re-tint PDF brand chrome.
- Pros: One coherent brand pass; matches how the token system + shared chrome actually work; no
  half-branded surfaces.
- Cons: Largest scope; most E2E/POM rewrites.

### B: Admin-only, minimal
- Confine changes to admin surfaces + the Users page.
- Pros: Smaller blast radius.
- Cons: The dark sidebar + tokens are shared chrome — can't go dark for admins only without a
  separate per-role layout; would re-introduce visible brand divergence between roles. Rejected.

### C: Global chrome re-tint only (no structural changes)
- Just swap palette + logos + footer, keep light sidebar, keep zebra tables, keep inline actions.
- Pros: Lowest risk.
- Cons: Ignores explicit client asks (dark sidebar, de-zebra, kebab, filter card). Rejected.

## Decision

**Approach A.** Spec `037-brand-alignment` created and reviewed (REVIEW-SPEC.md → SOUND).

Resolved during the session:
- **Scope:** Full re-sweep (all roles), Users page as the reference component treatment.
- **Actions column:** Option A — "Editar" visible + "⋯" kebab (Reenviar invitación / Restablecer /
  Inhabilitar; Inhabilitar red-outline). Routes/verbs unchanged; only relocated.
- **Footer:** Single official combined image (`Fooder-general.png`) with `#FFC729` top border.
  Partner set intentionally changes (drops "10 años", adds "De la mano con su PYME") — OQ-B accepted.
- **Dark sidebar applies to all roles**, including applicant — OQ-A accepted.
- **PDF teal delta:** "It must match now" → PDF brand-**asset** re-tint to `#008A9E` is in scope
  (narrow, documented exception to spec 019 FR-039); PDF generation pipeline/layout/content untouched.

Official palette pinned: primary `#008A9E`, light teal `#42AFA8`, orange `#F9A61C`, yellow `#FFC729`;
neutrals page `#F6F8FA` / card `#FFFFFF` / border `#DDE5E8` / text `#1F2933` / muted `#64748B`;
sidebar `#12343B` / hover `#174A53`; success `#168A4A` / danger `#D92D20`. Cream table-zebra removed.

## Open Threads

- Raster-as-provided vs. request vector original if the auth-hero vertical logo renders soft (OQ-001).
- Exact sidebar white-container treatment — full-bleed white pill vs. subtle off-white card (OQ-002).
- Is `#F9A61C` orange wired to any existing status today, or held purely in reserve? (OQ-003).
- Should the PDF partner-strip also adopt the new official partner set, or teal re-tint only? (OQ-004).
- Page background shifts to off-white `#F6F8FA` (spec 019 chose pure white) — confirm it reads well
  across dense admin tables.
- E2E selector churn from the kebab + de-zebra + dark-sidebar restructure — POM rewrites budgeted;
  per-surface brand assertions replace the old per-sponsor-SVG footer assertions.
- Reuse spec 019/011 audit scripts (hex-grep gate, axe AA, reduced-motion, asset-weight, perf
  baseline) for verification — confirm during planning.
