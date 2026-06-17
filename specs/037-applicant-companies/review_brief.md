# Review Brief: Applicant Companies (037)

**Spec:** specs/037-applicant-companies/spec.md
**Generated:** 2026-06-17

> Reviewer's guide to scope and key decisions. See full spec for details.

---

## Feature Overview

Applicants today free-type a company name when starting a funding application, producing inconsistent data. This feature replaces that free-text field with a controlled dropdown of companies that **administrators** assign to each applicant. A new admin-managed Company concept (just a name, for now) is introduced. A single company auto-selects; multiple companies force an explicit choice. Each application stores both a company reference and a frozen name snapshot, so historical applications keep the name they were created with even after the company is later renamed. Scope is the Applicant (Solicitante) role only.

## Scope Boundaries

- **In scope:** New admin-managed Company (name only); applicant company dropdown on submission (auto-select / explicit-choice / zero-company block); admin create-with-≥1-company, add, rename, soft-archive/unarchive with a last-active floor; required company column on the batch CSV; name snapshot for historical preservation; server-side ownership/active validation; admin audit events.
- **Out of scope:** Company attributes beyond name; applicant self-service company management; backfill/migration of existing data; reviewer/admin application-surface changes beyond displaying the snapshot; linking Company to the supplier catalog or to `LegalId`.
- **Why these boundaries:** Keep the change focused on the applicant submission path and admin management; reuse the existing name column for history rather than building versioning.

## Critical Decisions

### Greenfield rollout (no backfill)
- **Choice:** Do not migrate existing applicants/applications. The application→company reference is nullable; pre-existing applications keep their stored free-text name with no reference.
- **Trade-off:** Pre-existing applicants start with zero companies and cannot create new submissions until an admin adds one.
- **Feedback:** Is "existing applicants can't submit until an admin adds a company" acceptable operationally, or is a one-time backfill wanted later?

### Name snapshot instead of company versioning
- **Choice:** Copy the company name onto each application at creation; re-copy on change while `Draft`; freeze at submission.
- **Trade-off:** Slight denormalization (a name stored per application) vs. a versioned-company model.
- **Feedback:** Confirm the snapshot approach satisfies the "preserve name at time of submission" rule for your audit/legal needs.

### Soft archive + last-active floor
- **Choice:** Companies are never hard-deleted; admins archive/unarchive, and the system blocks archiving an applicant's last active company.
- **Trade-off:** No way to fully remove a mistakenly-created company (only archive it).
- **Feedback:** Is archive-only sufficient, or is hard-delete-when-never-used needed?

### Company editable while Draft
- **Choice:** The applicant can change the selected company until submission, then it freezes.
- **Trade-off:** Slightly more surface than locking at creation (must re-validate ownership/active on change).
- **Feedback:** Confirm applicants should be able to switch companies on a draft rather than rebuild it.

## Areas of Potential Disagreement

### Batch CSV gains a required column
- **Decision:** Append a required `Nombre de la empresa` column to the spec-034 import; each created applicant gets their first company from it.
- **Why this might be controversial:** Enlarges the feature and changes an existing import contract/template.
- **Alternative view:** Leave batch out of scope and have admins add companies afterward.
- **Seeking input on:** Confirm batch must satisfy the "≥1 company at creation" rule inline (current decision) vs. defer.

## Naming Decisions

| Item | Name | Context |
|------|------|---------|
| New entity | Company (Empresa) | Admin-managed, owned by one applicant, name-only |
| Batch CSV column | Nombre de la empresa | Appended last in the import header/template |
| Dropdown placeholder | Seleccione una empresa… | Multi-company case, no default selection |
| Lifecycle | Active / Archived | Soft archive; archived hidden from new-submission dropdown |

## Open Questions

- [ ] Admin UI placement for the per-applicant company list (inline on user Edit vs. dedicated sub-surface) — HOW, deferred to planning.
- [ ] Audit-event naming prefix (likely `company.*`) — confirm during planning to match `fund.*`/`process.*` conventions.

## Risk Areas

| Risk | Impact | Mitigation |
|------|--------|------------|
| Existing applicants blocked from submitting (no companies) | Med | Documented as accepted; admins add companies; backfill remains an option later |
| Cross-applicant company selection via forged request | High | Server-side ownership + active validation (FR-018/019), no-disclosure rejection |
| Archived-while-draft leaves an unsubmittable draft | Low | FR-020 requires re-selecting an active company before submit |
| Batch import contract change breaks existing templates | Low | Template regenerated with the new column; per-row validation mirrors spec 034 |

---
*Share with reviewers before implementation.*
