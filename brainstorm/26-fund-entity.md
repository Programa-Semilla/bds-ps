# Brainstorm: Fund (Fondo) Entity

**Date:** 2026-06-09
**Status:** spec-created
**Spec:** specs/029-fund-entity/

## Problem Framing

The funding hierarchy starts at **Process** today; nothing above it expresses *which fund a Process draws from* or *what regulation governs it*. A seed (`brainstorm/seeds/fondo_entity_seed.md`) asked to introduce a **Fund** (es-CR: *Fondo*) as the top-level container: **Fund → Process → Group → members**, where each Process belongs to exactly one Fund and a Fund carries name, description, and a regulation PDF.

A key part of the session was reconciling the seed's generic vocabulary with the real model: `Process` is an exact match; "Template" is `Plantilla`/`ProcessPlantilla`; "Group" is a **reviewer-access** partition (spec 016), not an applicant grouping; "Participant" has no clean entity (loosely `ApplicationUser` via `UserGroupMembership`); and **Fund did not exist**. So the change is cleanly a new parent aggregate above Process plus a required FK — the Groups/Participants chain already hangs off Process and is untouched.

## Approaches Considered

### A: New `Fund` aggregate + required FK + status enum + spec-014 PDF storage (chosen)
- Pros: Mirrors how `Process` is built and how attachments already work; reuses `IObjectStorage` (spec 014), `AdminAuditEvent` (016/017/020), toast/dialog (024), es-CR; no new deps; satisfies Rich Domain Model (archive/reactivate behaviors).
- Cons: Cascade-freeze-on-archive touches several read/write paths (submit, edit, review queues).

### B: Fund as a lightweight lookup table (no status, no behavior)
- Pros: Minimal.
- Cons: Can't support the requested Active/Archived lifecycle; rejected.

### C: Embed regulation as a DB blob
- Pros: One fewer storage hop.
- Cons: Violates the spec-014 "no DB blobs, use object storage" convention; rejected.

## Decision

Chose **A**. Scope locked by four user decisions:
1. **Pre-production** → no migration; `Process.FundId` is required (NOT NULL) from day one; seeds create a Fund and attach seed Processes.
2. **Regulation PDF** → optional, single, replaceable (no version history).
3. **Lifecycle** → Active/Archived; **archiving freezes all activity beneath the Fund** and hides it + its Processes from non-admins (admins reach it via a status filter); no hard delete.
4. **Reporting** → Fund as a filter dimension on existing admin reports/exports + Fund column/filter on the Process list + Fund detail page; deeper Fund→Groups/Participants rollups deferred.
5. **Regulation is applicant-facing** (downloadable in a Process's context via spec-014 signed URL), not admin-only.

Spec written (`specs/029-fund-entity/spec.md`): 5 prioritized user stories (US1/US2 = MVP), FR-001..016, SC-001..006. Gate review **SOUND** (`REVIEW-SPEC.md`), reviewer brief in `review_brief.md`. Branch `029-fund-entity`.

## Open Threads

- Exact set of actions disabled when a Fund is archived — deferred to plan; scoped to the Process state model.
- "Participant under a Fund" semantics — Group holds reviewers in this codebase, not applicants; any future Fund→participant report must define which population it counts.
- Whether Processes-per-Fund reporting is sufficient for v1, or stakeholders expect the seed's participant drill-down.
- Process Fund selector ordering (by name?) and how archived-Fund Processes render in admin views (read-only badge vs hidden toggle).
- New `fund-regulations` spec-014 FileCategory size cap value — pin during planning.
