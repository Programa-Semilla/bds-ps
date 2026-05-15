---
name: 18-feedback-session-may13
description: Brainstorm bundling 26 refinements from the May-13 stakeholder feedback session into a single spec (021).
metadata:
  type: brainstorm
  status: spec-created
  spec: specs/021-feedback-session-may13/
---

# Brainstorm: Feedback Session May-13

**Date:** 2026-05-13
**Status:** spec-created
**Spec:** specs/021-feedback-session-may13/

## Problem Framing

A May-13 refinement session (Milena Arias, Pao Rodríguez Marín, Danny Pérez) produced 26 named items spanning architectural restructure, role expansion, data-quality hardening, UX polish, copy/terminology updates, and one bug fix. Stakeholders explicitly asked for the items to be addressed in *"single shot"*. Source artefact: `brainstorm/seeds/Refinamiento de Requerimientos - 2026_05_13 12_59 CST - Notes by Gemini (Spanish).pdf`.

The platform currently has no annual-cycle concept (only `Group`, spec 016), exposes raw counters (*"Solicitud N.º N"*), frames itself around *financiamiento* rather than *acompañamiento*, places `Impact` on `Item` (the meeting wants it on a higher-level grouping, called *ficha*), has no centralised supplier-administration role, lacks input masks / autosave / required-field markers / submit-time confirmation / forgot-password, and has a defect where soft-deleted Applications continue to display as active. Stage-expiry, reminder emails, public landing, and admin-dashboard repivot were all also called out as gaps.

## Approaches Considered

### A: Atomic mega-spec, single delivery (CHOSEN)
- Pros: Matches stakeholder *"single shot"* directive; every committed refinement ships together; copy/terminology arrives with the feature changes; no half-state.
- Cons: Largest spec in project history (34 FRs / 16 SCs / 8 user stories); biggest review surface.

### B: One spec, two declared phases
- Pros: Lower internal-risk via explicit P1 schema/role then P2 UX/copy.
- Cons: Extra ceremony; arbitrary cut line; P2 work depends on P1 entities anyway.

### C: Scope-trim now, defer polish to a future spec
- Pros: Smaller blast radius.
- Cons: Contradicts the stakeholder *"single shot"* directive.

## Decision

**Approach A.** One mega-spec (021) covering all 26 items, internally prioritized P1/P2/P3 so the plan phase can sequence delivery cleanly. The spec is `specs/021-feedback-session-may13/spec.md`, review report at `REVIEW-SPEC.md` (verdict: **SOUND** at first pass), reviewer guide at `review_brief.md`, quality checklist at `checklists/requirements.md`.

### Key resolutions during brainstorm

- **Proceso = new aggregate above Group.** *Crocus*, *Nexo* are example free-text Process names (in-progress program cycles), not enum values. Existing Groups migrate under a seeded *"Migración inicial"* Process.
- **Plantilla = config bundle referencing ImpactTemplates, with copy-on-assign.** Assigning a base Plantilla to a Process creates an immutable `ProcessPlantilla` snapshot.
- **Impact moves to Application (single ImpactTemplate per Application).** *Item.Impact* dropped outright — no production data, no migration.
- **Fourth role = SupplierAdmin**, scoped strictly to Supplier + SupplierBranch CRUD + `IsCompliant` toggle. Denied on every other admin surface (403 + audit event).
- **PublicCode = `^[A-HJ-NP-Z2-9]{4}-[A-HJ-NP-Z2-9]{4}$`** (8 chars, two 4-char groups, dictation-safe alphabet, excludes 0/O/1/I/L).
- **Submit gating + dedicated `/review` page** (not modal); on-blur autosave with `✓ Guardado` indicator; profile self-edit on FirstName/LastName/Phone/Address, admin-only on Email/Role/Group/CodigoPersonal.
- **Province + Cantón cascading dropdowns** (7 provinces, ~82 cantones); foreign supplier addresses out of scope.
- **Stage expiry = global default + per-Process override**; hard-block via HTTP 422; T-72h / T-24h / expiry email reminders via existing SMTP wiring through a hosted background service.
- **Acompañamiento pivot:** `"¿Listo para acelerar tu negocio?"` + `"Iniciar acompañamiento"`; *"financiamiento"* removed from applicant-facing surfaces (legal Funding Agreement PDF retains the term). Greeting becomes `"Hola, {{Nombre}}"`.
- **Public `/` landing scaffold ships in 021** with placeholder slots (reglamento, ejemplo, sponsor strip from spec 019).
- **Admin dashboard repivot:** *Personas activas* + *Fondos entregados* KPIs added; pending-quotation tile moves to reviewer dashboard; admin user list group filter becomes cascading Process → Group.
- **BCCR exchange-rate auto-fetch + Tropic AI quotation extraction = OUT of scope (research only).** OTP, guided tour, user-initiated email-change request flow, foreign supplier addresses, visual-regression tooling = OUT of scope.

## Open Threads

- Plantilla assignment cardinality per Process — one-to-one (default) vs many-to-one (one Process stacks multiple snapshots for different Application kinds) — pin in `/speckit-plan` (OQ-1).
- Process closure semantics on `FundingAgreement` aftermath — does closing a Process freeze its signed agreements? Default = yes; revisit if operations pushes back (OQ-2).
- Stage-expiry override granularity — per-Process only (default) vs also per-Plantilla (OQ-3).
- PublicCode rendering on the legacy Funding Agreement PDF template (spec 018) — template field swap vs footnote (OQ-4).
- Reglamento + ejemplo files — content ownership and authoring source pending; admin team vs Programa Semilla operations (OQ-5).
- Email-reminder cadence (T-72h / T-24h / expiry) — fixed (default) vs admin-configurable (OQ-6).
- SupplierAdmin scope — full CRUD on suppliers (default) vs validate-only-existing (OQ-7).
- Hint copy authorship for FR-020's initial set — designer / copywriter delivery pending (OQ-8).
- Process audit-event coverage — extends `AdminAuditEvent` (spec 016 pattern); pin in plan (OQ-9).
- Provincia *"Otro/Extranjero"* handling — block in UI (default) vs catalog row; revisit if foreign suppliers surface (OQ-10).
- Admin-override path for expired stage-windows — whether the HTTP 422 hard-block should be overridable from the admin panel; surfaced during reviewer-brief drafting, not yet resolved.
- BCCR exchange-rate auto-fetch + Tropic AI quotation extraction — research-only in 021; need future brainstorm / spec to productize.
- Single-spec scope vs. architectural / UX split — confirmed in brainstorm but reviewers may push back; reviewer brief flags this for stakeholder review.
