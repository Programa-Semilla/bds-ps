# Review Brief: Fund (Fondo) Entity

**Spec:** specs/029-fund-entity/spec.md
**Generated:** 2026-06-09

> Reviewer's guide to scope and key decisions. See full spec for details.

---

## Feature Overview

Introduces **Fund** (es-CR: *Fondo*) as the new top-level container above Process: **Fund → Process → Group → members**. Each Process must belong to exactly one Fund. A Fund carries a name, a description, and an optional regulation PDF that applicants can download from any Process under it. Funds are admin-managed and follow an Active/Archived lifecycle, where archiving freezes all activity beneath the Fund. The system is pre-production, so there is no data migration — seed data creates a Fund and attaches existing seed Processes.

## Scope Boundaries

- **In scope:** Admin Fund CRUD; regulation PDF upload/replace/remove; required Fund selector on Process create/edit; Active/Archived lifecycle with cascade freeze; applicant regulation download; Fund column/filter on the Process list and a Fund filter on existing reports; Fund detail page listing its Processes.
- **Out of scope:** Fund→Groups/Participants rollup reports; multiple regulation docs + versioning; per-Fund permission scoping; regulation on landing/emails; hard delete of Funds.
- **Why these boundaries:** Keep the change to a single new aggregate plus a required FK; defer drill-down reporting and permission work that the seed framed loosely.

## Critical Decisions

### Required Process→Fund FK from day one
- **Choice:** `Process.FundId` is NOT NULL; no nullable/gradual phase.
- **Trade-off:** Cleanest invariant, enabled only because the system is pre-production.
- **Feedback:** Confirm there is truly no environment with Processes that would need backfill.

### Archive = cascade freeze, not just "no new attach"
- **Choice:** Archiving a Fund freezes its Processes and all downstream submit/edit/review actions, and hides them from non-admins.
- **Trade-off:** Powerful but broad; in-flight applications freeze immediately.
- **Feedback:** Confirm freezing in-flight applicant/reviewer work mid-cycle is desired (vs. blocking only new work).

### Regulation is applicant-facing, single, replaceable, no history
- **Choice:** One PDF per Fund, downloadable by applicants via time-limited link; replacing supersedes the old file with no version trail.
- **Trade-off:** Simple; loses an audit trail of regulation changes.
- **Feedback:** Confirm no need to retain prior regulation versions.

## Areas of Potential Disagreement

### "Participant under a Fund" meaning
- **Decision:** Deferred all Fund→Groups/Participants rollup reporting; MVP reporting is Processes-per-Fund + filters.
- **Why this might be controversial:** The seed explicitly asked to "view all Participants indirectly associated with a Fund."
- **Alternative view:** Some stakeholders may expect participant drill-down in v1.
- **Seeking input on:** Is Processes-level reporting enough for the first release?

### Group holds reviewers, not applicants
- **Decision:** Spec notes that in this codebase a Group scopes reviewer access; the seed implied applicant grouping.
- **Why this might be controversial:** "Participants under a Fund" could mean applicants, reviewers, or both.
- **Seeking input on:** Which population should any future Fund participant report count?

## Naming Decisions

| Item | Name | Context |
|------|------|---------|
| New entity | Fund (UI: *Fondo*) | Top-level container above Process |
| Status values | Active / Archived | Fund lifecycle |
| Regulation doc | *Reglamento* (PDF) | Single optional file per Fund |
| Storage category | `fund-regulations` | New spec-014 FileCategory, PDF-only, size-capped |
| Admin nav entry | *Fondos* | Under existing admin sidebar group |

## Open Questions

- [ ] Exact set of actions disabled when a Fund is archived (deferred to plan, scoped to the Process state model).
- [ ] Whether to order the Process Fund selector by name and how archived-Fund Processes render in admin views.

## Risk Areas

| Risk | Impact | Mitigation |
|------|--------|------------|
| Cascade freeze touches many code paths (submit, edit, review queues) | High | Enumerate affected actions in plan.md; lean on existing Process state model |
| Required FK breaks existing seed/test data | Medium | Update seeds to create a Fund and attach seed Processes (called out in spec) |
| Regulation download must respect storage serving + access rules | Medium | Reuse spec-014 signed-URL serving; gate on Fund Active + PDF present |

---
*Share with reviewers before implementation.*
