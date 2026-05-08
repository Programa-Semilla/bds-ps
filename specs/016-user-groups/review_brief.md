# Review Brief: Group-Scoped Reviewer Access

**Spec:** specs/016-user-groups/spec.md
**Generated:** 2026-05-07

> Reviewer's guide to scope and key decisions. See full spec for details.

---

## Feature Overview

Reviewers should not see every applicant. This feature introduces a flat catalog of named groups (admin-managed). Every non-admin user (Applicant or Reviewer) gets one or more group memberships, assigned by admin at user-create or user-edit. A reviewer's queue, signing inbox, application search, and detail-page authorization are all scoped to applicants whose group set intersects the reviewer's group set. Admins bypass the filter and see everything. Outcome: territorial / cohort-based reviewer assignment without per-application routing.

## Scope Boundaries

- **In scope:** Group entity (CRUD), user-group membership (many-to-many on `ApplicationUser`), admin user form changes (multi-select group selector + ≥1-group validation for non-admin roles), reviewer-side filtering on queue / detail page / signing inbox / search, admin bypass, audit recording for group lifecycle and membership changes, es-CR localization for new copy.
- **Out of scope:** Hierarchical groups, multi-dimensional tagging, applicant self-service group selection, per-application reviewer routing, group-based filters in admin reports, data migration (system non-prod), per-group quotas/rate limits, cross-group transfer workflows.
- **Why these boundaries:** Match the smallest set of behaviors that delivers the stated outcome. Hierarchy and tagging are deferrable; reports and quotas belong to other specs.

## Critical Decisions

### Visibility rule = any-group overlap
- **Choice:** Reviewer sees applicant if their group sets share at least one group (set intersection ≠ ∅).
- **Trade-off:** Most permissive option among the alternatives considered. Strict subset and primary-group-only were rejected as too rigid for typical sharing scenarios.
- **Feedback:** Confirm permissiveness is acceptable for compliance / privacy posture.

### Groups attach to `ApplicationUser`, applicants inherit
- **Choice:** Single source of truth on the user. The `Applicant` entity does not carry its own group list; visibility joins through `Applicant.UserId → ApplicationUser → Groups`.
- **Trade-off:** Cleaner schema, single assignment UI. Decoupled "reviewer access groups" vs "applicant business groups" is intentionally not modeled.
- **Feedback:** Confirm there is no near-term need for a business-segmentation taxonomy that diverges from access scoping.

### Cascade-delete is allowed to leave non-admin users with zero groups
- **Choice:** Deleting a group removes membership rows. Users formerly in that group only end up with zero groups; system does not block login, does not auto-reassign, does not force the admin to clean up first.
- **Trade-off:** Simpler delete UX; pragmatic in a non-prod system. Trade is that the "non-admin must belong to ≥1 group" rule is enforced at create/edit submit but not protected from cascade erosion.
- **Feedback:** Confirm this asymmetric enforcement is acceptable.

### Admin role never carries group memberships
- **Choice:** Admin is groupless by construction. Promoting a Reviewer to Admin discards the prior memberships on save. The form hides the selector when role = Admin.
- **Trade-off:** Simple invariant ("Admin == bypass"). Loses the option of using groups on admins as an organizational label.
- **Feedback:** Confirm there is no need for admin-side organizational tagging.

## Areas of Potential Disagreement

> Decisions or approaches where reasonable reviewers might push back.

### Cascade-delete + zero-group state allowed
- **Decision:** A non-admin user can end up with zero groups; UI shows them as such; reviewer queue is empty; admin still sees their applicant data.
- **Why this might be controversial:** Violates the literal reading of "non-admin users must belong to one or more groups" once cascade has occurred. Some reviewers may prefer a hard invariant: block delete or auto-disable login.
- **Alternative view:** Block delete unless every member has another group, or auto-disable accounts that fall to zero.
- **Seeking input on:** Whether the asymmetric enforcement (create/edit yes, post-cascade no) is acceptable, or whether the rule should be tightened.

### Search scope (FR-014) is broad
- **Decision:** "Reviewer-facing applicant and application search" is filtered identically to the queue.
- **Why this might be controversial:** It assumes such a search surface exists today, or that it will be group-aware on first introduction. If the codebase only has a queue-level filter today, this requirement may be aspirational.
- **Alternative view:** Restrict the FR to the existing queue + detail + signing-inbox surfaces and let any future search inherit the filter at the time it is added.
- **Seeking input on:** Plan-time confirmation of which search surfaces map to FR-014.

### Audit recording deferred to plan
- **Decision:** NFR-005 mandates audit recording but defers the mechanism (reuse existing or add minimal) to the plan. Open Question OQ-001 captures this.
- **Why this might be controversial:** Some reviewers prefer specs to lock the audit policy explicitly (retention, surface, access).
- **Alternative view:** Pin the mechanism (e.g., reuse the existing audit log) at spec time.
- **Seeking input on:** Whether deferral is acceptable.

## Naming Decisions

| Item | Name | Context |
|------|------|---------|
| Domain entity | Group | The named partition; flat list. |
| Junction | UserGroup (or `ApplicationUserGroup`) | Many-to-many membership row. |
| Group attribute | Name | Single field beyond identity; case-insensitive unique. |
| Demo seed groups | Norte, Sur, Centro | Working assumption; final list locked at plan time. |

## Open Questions

- [ ] OQ-001: Audit mechanism — reuse existing or add new (deferred to plan).
- [ ] OQ-002: Final demo seed group names (Norte / Sur / Centro is the working list).

## Risk Areas

| Risk | Impact | Mitigation |
|------|--------|------------|
| In-memory post-filtering of an unscoped result set | High | NFR-001 mandates DB-level scoping for all listing endpoints. |
| Detail-page bypass via URL tampering | High | NFR-002 mandates server-side enforcement; FR-012 returns 403. |
| Concurrency conflict on simultaneous group-membership edits | Medium | EC-007 binds to the existing optimistic-concurrency token on the user record. |
| Reviewer keeps stale claims after admin removes a group | Medium | NFR-003 requires "next request" effectiveness; no logout required. |
| Cascade-delete leaves users in zero-group state | Medium (deliberate) | Documented in FR-005 + EC-001/EC-002; flagged in this brief for reviewer pushback. |

---
*Share with reviewers before implementation.*
