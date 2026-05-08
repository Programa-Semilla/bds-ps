# Brainstorm: Group-Scoped Reviewer Access

**Date:** 2026-05-07
**Status:** spec-created
**Spec:** specs/016-user-groups/

## Problem Framing

The platform currently has a flat reviewer model: every Reviewer (and admin-acting-as-reviewer) sees every Application in the queue, in the signing inbox, and via direct application URLs. As the operation scales across territories or cohorts, that posture stops working — reviewers should only handle the applicants they are assigned to. The user's framing makes the rule explicit: "non-admin users must belong to one or more groups, so reviewers can review only applicants from the same groups assigned. Only admins can create groups. Groups is assigned by the time of creation or later when editing an user."

The existing identity model is three roles (`Applicant`, `Reviewer`, `Admin`) on `ApplicationUser`, with an admin-implies-reviewer claims transformation. Applicants are an `Applicant` entity linked to an `ApplicationUser` by `UserId`. There is no Group concept. Visibility scoping today happens only at the per-application authorization helpers on the `Application` entity, which expose an `isReviewerAssignedToThisApplication` flag that callers compute (currently always-true for any reviewer).

## Approaches Considered

### A: Groups on `ApplicationUser` only (Selected)

- New `Group` entity (Id + unique Name, case-insensitive).
- Many-to-many join `UserGroup` (or `ApplicationUserGroup`) on `ApplicationUser`.
- Applicants inherit groups via `Applicant.UserId → ApplicationUser → Groups`.
- Visibility filter: `applicantUser.Groups ∩ reviewerUser.Groups ≠ ∅`. Admins bypass.
- Reviewer-facing surfaces — queue, application detail, signing inbox, applicant/application search — apply the filter at the DB query level (NFR-001).
- Detail-page authorization (`Application` helpers) extends `isReviewerAssignedToThisApplication` with a group-overlap check; URL tampering returns 403 (NFR-002).
- Admin user CRUD form (`AdminUsersController` + `UserAdministrationService`) gains a multi-select group selector. Validation: ≥1 group when role is `Applicant` or `Reviewer`. Group selector hidden when role = `Admin`; promoting to Admin discards prior memberships.
- Group cascade-delete: removes membership rows; users left with zero groups stay logged in but show empty queue. The "≥1 group" rule is enforced at create/edit submit, not protected from cascade erosion (deliberate trade-off).
- Pros: single source of truth on the User; uniform for Reviewer and Applicant; uses the existing `Applicant.UserId` link; minimal schema delta (one entity + one join); reuses the existing optimistic-concurrency token on `ApplicationUser` for membership writes (constitution Quality Gate).
- Cons: assumes the applicant-as-user link is canonical (already true in the codebase). If a future need emerges for "applicant business segmentation" distinct from "reviewer access scoping", the model would have to be split.

### B: Groups on both `ApplicationUser` and `Applicant`

- Two separate group lists: one on `ApplicationUser` for reviewer access, one on `Applicant` for business segmentation.
- Pros: decouples access from business segmentation; flexibility for cohort vs territory taxonomies.
- Cons: two assignment UIs, two truths, easy to drift, no current need. Rejected as YAGNI per constitution Principle VI.

### C: Roles-with-suffixes (no Group entity)

- Encode groups as Identity role names: `Reviewer:Norte`, `Applicant:Norte`.
- Pros: zero schema delta.
- Cons: cannot express multi-group cleanly; admin CRUD becomes string-mangling; breaks `AdminImpliesReviewerClaimsTransformation`. Hacky. Rejected.

## Decision

Approach **A**. Single `Group` entity, many-to-many on `ApplicationUser`, applicants inherit through the `UserId` link. Visibility = group overlap; admin bypasses.

Key choices locked during the session:

- Visibility rule: any-group overlap (set intersection ≠ ∅). Stricter alternatives (subset, primary-group-only) rejected as too rigid.
- Applicant assignment: admin assigns at user-create and user-edit (not self-service, not auto-derived).
- Migration: none. System is non-production; existing data may be dropped and re-seeded.
- Group structure: flat list (no hierarchy, no multi-dimensional tagging).
- Group lifecycle: cascade delete; non-admin users may end up with zero groups; admin sees them in the user list with their (possibly empty) group set; no auto-disable.
- Admin scope: admins never carry groups; promotion to Admin discards memberships; demotion to non-admin requires ≥1 group at submit time.
- Filter scope: queue list, application detail page (server-side authorization, not just hidden), signing inbox, reviewer-facing search.
- Group fields: Name only (unique). No description, no code, no active flag.

Spec review (REVIEW-SPEC.md) returned SOUND. One Important note tightened inline — EC-007 now binds the membership write to the existing optimistic-concurrency token on the user record per the constitution Quality Gate. Three optional plan-time notes carried forward in REVIEW-SPEC.md: confirm reviewer-facing search surfaces for FR-014, lock the audit mechanism for NFR-005, and consider lifting the "≥1 group for non-admin" invariant into the domain entity per Rich Domain Model.

## Open Threads

- OQ-001 (audit mechanism): reuse existing audit pathway or add a minimal one — decided at plan time.
- OQ-002 (demo seed group names): working list is "Norte", "Sur", "Centro"; final names locked at plan time.
- FR-014 search-surface inventory: confirm at plan time which existing reviewer-facing search endpoints exist; if none, restate FR-014 as forward-looking.
- Domain-level placement of the "≥1 group for non-admin" invariant — currently form-level; consider lifting into `User.SetGroups(role, groups)` per constitution Principle II.
