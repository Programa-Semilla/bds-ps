---
name: 34-feedback-3-provider-compliance
description: Brainstorm decomposing feedback round 3 (18-area unified requirements) and specifying slice A — Auditor role + provider regulatory compliance model (spec 038).
metadata:
  type: brainstorm
  status: spec-created
  spec: specs/038-auditor-provider-compliance/
---

# Brainstorm: Feedback-3 Decomposition + Provider Compliance (Slice A)

**Date:** 2026-06-17
**Status:** spec-created
**Spec:** specs/038-auditor-provider-compliance/

## Problem Framing

Feedback round 3 arrived as a single 2012-line unified requirements doc
(`seeds/feedback-3/AI_Coding_Agent_Unified_Requirements.md`) spanning **18 feature areas / 6 phases**:
fund-process windows, per-user funding limits, impact/category template tweaks, item-line + user-form
ordering, password-recovery fix, a new **Auditor** role, a full provider-compliance overhaul (enumerated
Hacienda/CCSS/SICOP statuses, PME/PYME, warnings, audit trail, freshness), a multi-criteria supplier
recommendation algorithm, an auditor application-workflow stage with checklists + PDF confirmation, daily
Hacienda API sync, and an applicant timeline/percentage-progress visual. Too large for one spec, with hard
cross-dependencies and an external API integration — unlike the May-13 round (which was one mega-spec).

## Approaches Considered

### A: One mega-spec (the May-13 precedent)
- Pros: single delivery, no coordination overhead.
- Cons: this round is far larger, has real dependency ordering (algorithm needs the new enums; workflow +
  freshness need the Auditor role), and includes an external API job — a mega-spec would be unreviewable
  and un-shippable incrementally.

### B: Decompose into ~8 dependency-ordered slices (CHOSEN)
- Pros: each slice independently specifiable/shippable; matches how this repo ships features; foundation-first
  ordering keeps later slices unblocked.
- Cons: needs a mechanism to carry context across many future sessions.

## Decision

**Approach B.** Sliced into A–H (see `seeds/feedback-3/00-decomposition.md`). **Context-carry mechanism:**
one **decomposition index** that *points* into the master doc by section number (no content duplication →
no drift); dependency context for already-shipped slices comes from their `specs/NNN/spec.md` + CLAUDE.md
*Recent Changes* (the repo's existing cross-feature channel), not from per-slice seed copies. Chose this over
exporting eight self-contained per-slice seeds (which would duplicate ~2000 lines and drift).

Then brainstormed **slice A** to a spec.

### Key resolutions during brainstorm (slice A)

- **Auditor vs existing SupplierAdmin role:** rename/absorb — SupplierAdmin *becomes* Auditor (members
  migrate, role no longer seeded). Avoids two near-duplicate roles. (Option a of three.)
- **Compliance model:** replace 3 boolean flags with **nullable enumerated statuses** using the **exact
  Spanish strings** (§13, preserved verbatim); `SICOP` canonical, `CCOP` alias dropped; **remove**
  electronic-invoice control entirely; add `IsPmeOrPyme`. **Greenfield, no backfill** (matches repo
  convention) — old true/false not translated.
- **A/D boundary:** A *tracks + displays* freshness (audit trail, per-field last-reviewed metadata, the
  "reviewed — no change / re-authorize" action) but does **not** enforce. D adds the 1-month staleness
  *block* + the daily Hacienda API sync.
- **Warnings:** auditors author/edit; reviewers view-only; informational, never blocks.
- **New-provider notification:** email-only, **direct-send** (spec-033 invitation pattern), bypassing the
  application-scoped outbox (event is provider-scoped); non-prod allowlist applies. No in-app center.
- PME/PYME **scoring** deferred to B; freshness **blocking** + Hacienda API deferred to D; auditor
  **workflow stage** deferred to C.

Spec 038 written, reviewed (REVIEW-SPEC.md verdict: **SOUND**), review_brief.md generated.

## Open Threads

- Slices B–H remain unspecified; B (recommendation algorithm) is the natural next foundation-dependent slice.
- Spec-038 plan-time decisions: es-CR Auditor display label; "reviewed — no change" availability before a
  value exists; warning-note max length; audit-trail storage approach (extend `AdminAuditEvent` vs dedicated
  table).
- Role-rename ripple: inventory every `SupplierAdmin` reference (auth, seeds, E2E fixtures) at plan.
