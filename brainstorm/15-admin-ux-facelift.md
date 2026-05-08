# Brainstorm: Admin UX/UI Facelift

**Date:** 2026-05-08
**Status:** spec-created
**Spec:** specs/017-admin-ux-facelift/

## Problem Framing

After spec 009 admin area shipped (Users + Reports stub), three more admin capabilities landed on the platform — spec 010 Reports content, spec 015 multi-currency (Currencies + Exchange Rates + Legacy Quotations queue), spec 016 Groups — each adding sidebar entries and routes but never updating the `/Admin` landing page. Today the landing page is a 3-card grid (Impact Templates / System Configuration / Suppliers) frozen pre-warm-modern; admins discover the rest via sidebar-spelunking. Spec 011 facelift sweep listed Admin in its inventory but didn't reach the new specs' surfaces and didn't lift the landing page itself into a wow moment.

The user's seed: *"Improve UX/UI of admin panel at /Admin. Starting concern: dashboard cards don't reflect full capabilities of admin user. Want facelift that surfaces all admin capabilities visibly, modern look matching warm-modern direction."*

Session shape: this was a NEW brainstorm (#15), not a revisit of #09 (admin scaffolding) or #11 (system-wide warm-modern facelift). Both prior brainstorms are explicit references in the new spec; neither covered the same territory. Spec 011's full sweep declared Admin in its inventory but never closed the loop on (a) the landing page itself, (b) new sub-surfaces from specs 015 + 016 that landed after spec 011 shipped.

## Strategic Decisions Made

### Surface focus

Three options were considered:

- **A: `/Admin` index only.** Cheapest. Closes the immediate "cards don't show capabilities" pain. Leaves sub-surfaces inconsistent with the new dashboard.
- **B: Index + admin sub-surface sweep at warm-modern bar.** Mirrors spec 011's full sweep philosophy.
- **C: Index + 'admin home dashboard' wow moment AND sub-surface sweep AND structural cleanup.** Largest scope.

**Decision: B+ (sweep at spec 011 wow-moment quality bar).** User chose "Index + admin sub-surface sweep" then chose "same bar as spec 011 wow moments" — combining option B with maximum quality bar. Pre-prod status justifies aggressive scope. Same risk profile as spec 011 / 008.

### Index treatment

Three options:

- **A: Grouped capability sections.** Cards under headers (Users & Access / Catalog / Operations). Scannable. No KPIs.
- **B: Flat 9-card grid.** Simplest. Risk of "card dump" feel.
- **C: Dashboard-style with KPIs + grouped cards.** Closest to spec 011 wow moments. KPI strip + 3 grouped sections.

**Decision: C, "dashboard-style with KPIs + grouped cards."** User wants the admin landing to feel like a peer of the applicant home and reviewer queue dashboards from spec 011. Promotes admin index from "menu of features" to "operational heads-up display."

### KPI set

Three options:

- **A: Action-oriented.** Pending suppliers / Pending legacy quotations / Aging applications / Active users. Three actionable + one health signal. Each KPI deep-links to a filtered surface.
- **B: Volume-oriented.** Active users / Total groups / Total suppliers / Total applications. Easier to wire (counts only). Less operationally useful.
- **C: Mixed health + admin breakdown.** Active users by role / Pending suppliers / Aging applications / Decided this month.

**Decision: A, "action-oriented."** "What needs my attention" beats "how big is the system." Three of four KPIs are actionable; the fourth (Active users) is the closest health-signal proxy.

### Sub-surface sweep depth

Three options:

- **A: Visual / token compliance + small UX wins.** Targeted nudges (filters, partials), no HTML rewrites unless needed.
- **B: Same bar as spec 011 wow moments.** HTML restructuring permitted, POM rewrites budgeted, motion catalog adherence.
- **C: Visual only — no behavior changes.** Apply tokens / partials only.

**Decision: B, "spec 011 wow-moment bar."** Mirrors the saved feedback memory `feedback_ui_quality_over_e2e_stability` — UX/UI quality > selector stability for facelift work. POM rewrites are budgeted across all 10 admin sub-surfaces.

### Pain point coverage (multi-select)

User selected ALL FOUR pain points as in-scope:

1. **Sidebar admin entries cluttered/long** (11 entries when Admin signed in) → US4: collapse under "Administración" section header with stable testids.
2. **Inconsistent capitalization / route names** (`AdminCurrencies` / `AdminExchangeRates` / `AdminLegacyQuotations` vs `Users` / `Groups` / `Reports` / `Suppliers`) → US5: route normalization, no redirect shim.
3. **Empty states / no-data messaging across admin tables** (generic "No data" across Currencies, Exchange Rates, Groups, Legacy Quotations) → US3: illustration-backed `_EmptyState` from spec 011's 9-scene set.
4. **Reports tabs UX (sub-tabs, KPI tiles, density)** → US6: pill-style filter chips matching reviewer-queue, animated KPI tickers, `--space-2` density.

### Schema changes

Two options:

- **A: Schema unchanged.** Mirrors spec 011 FR-067. KPIs are query-time aggregates. Cleanest scope.
- **B: Schema changes allowed if needed.** More flexibility, larger blast radius.

**Decision: A, "schema unchanged."** Zero edits to `src/FundingPlatform.Database/`. If planning surfaces an unavoidable need, escalate via `/speckit-spex-evolve`.

### Spec packaging

Three options:

- **A: Single mega-spec.** Mirrors spec 011 packaging.
- **B: Two specs sequenced.** Index dashboard first, sub-surface sweep + structural cleanup second.
- **C: Three specs.** Maximum independence.

**Decision: A, "single mega-spec."** Same risk profile as spec 011 / 008. Pre-prod justifies aggressive scope. Single sign-off gate, single E2E sweep, single PR.

## UX/UI Principles Applied

Inherits all principles from spec 008 (status is the spine, etc.) and spec 011 (brand presence is felt not announced, every wow moment earns its motion budget, density per audience). Adds one admin-area-specific principle:

10. **Capability completeness is a first-class invariant of the admin landing page.** When a new admin capability lands, the dashboard must add a card. *Spec mechanism:* edge-case enumeration + open-thread governance check (FR-007 implies but does not enforce; planning may add an explicit FR/SC).

## Phased Plan

This brainstorm produces **one spec immediately** (017 admin UX facelift):

- **Spec 017 (THIS spec, created):** `/Admin` dashboard + sub-surface sweep at spec 011 quality bar + empty states + sidebar grouping + route normalization + Reports tab UX refresh + activity feed (P3 optional).

No follow-up specs queued from this brainstorm. The deferred items captured as Open Threads below should be tracked but do not require their own spec.

## Risks & Anti-Patterns Captured

- **Mega-spec scope creep.** Mitigated by 10 OOS clauses + `ADMIN-SWEEP-CHECKLIST.md` deliverable per FR-008.
- **POM rewrite cost overrun across 10 sub-surfaces.** Saved feedback memory accepts the trade-off; planning sequences POM work per surface.
- **Dashboard rot when next admin capability lands** (a future spec ships without updating the dashboard's capability cards). Captured in edge cases; governance check is an open thread (whether to make it an explicit FR/SC).
- **`AdminAuditEvent` sparse in dev → activity feed never appears.** US7 hides the feed gracefully when empty (no empty rail).
- **Pending-supplier source ambiguity.** Assumption captured; planning pins the enum value.
- **Route 404 hits from external bookmarks.** Pre-prod fixture means none expected; no-redirect call is intentional.
- **Voice-guide drift.** SC-018 mandates per-string `BRAND-VOICE.md` review on every swept view.
- **Schema-unchanged constraint forces awkward query-time aggregation.** Documented escape hatch via `/speckit-spex-evolve` (FR-027).
- **Sidebar testid stability vs visual restructure.** FR-016 enforces all prior `sidebar-entry-*` slugs remain present and findable.

## Decision

A single executable feature was created as `specs/017-admin-ux-facelift/` covering the dashboard wow moment, sub-surface sweep, empty states, sidebar grouping, route normalization, Reports tab UX refresh, and a P3 optional activity feed. Spec passed `speckit-spex-gates-review-spec` review on first iteration with status SOUND (5/5 completeness, 4.5/5 clarity, 5/5 implementability, 5/5 testability, all 6 constitution principles aligned). Two minor planning-phase guard-rails were noted in `REVIEW-SPEC.md` (FR-015 implementation choice; pending-supplier failure-mode wording).

User approved the spec and chose to proceed to `/speckit-plan`.

## Open Threads

- FR-015: pin "section header click target" vs "Panel sub-entry inside the section" implementation choice during planning.
- Pending-supplier failure-mode (zero count / "—" placeholder / error tile) when source is missing or stale — pin during planning.
- Pending-supplier source enum value — confirm spec-013 supplier-status mapping during planning.
- Governance FR/SC for "future admin specs must update the dashboard's capability cards" — open question (currently captured only as an edge-case mitigation note, not enforceable).
- Naming the dashboard projection (e.g., `IAdminDashboardProjection`) in the spec vs deferring to plan.
- Section grouping cardinality — three sections (Usuarios y acceso / Catálogo / Operaciones) vs four (split Catálogo into entity catalog vs config catalog) — defaults to three; revisit if planning surfaces a stronger grouping argument.
- Whether the route normalization should also touch class names + namespaces (currently only route attributes) — defaults to attribute-only; revisit only if a future "admin module reorganization" spec is on the queue.
- Whether sub-surfaces that already passed spec 011's sweep need a fresh manual checklist walk vs a quick re-grep — defaults to manual walk per FR-009 + SC-007; planning may relax for surfaces with provably-clean spec-011 sweep history.
