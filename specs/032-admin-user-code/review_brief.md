# Review Brief: Admin-only user provisioning + unique applicant User Code

**Spec:** specs/032-admin-user-code/spec.md
**Generated:** 2026-06-11

> Reviewer's guide to scope and key decisions. See full spec for details.

---

## Feature Overview

Public self-registration is removed: accounts can only be created by an administrator, and the old `/Account/Register` URL returns 404. A new **User Code** — an admin-assigned, free-text, ≤50-character, unique identifier — is added to applicants and is required when creating/editing a Solicitante. The same User Code, plus the applicant's personal identification, becomes searchable through the existing single search box on every user/applicant surface: the admin users list, the reviewer queue (and its row-refresh), and the admin reports + applicants CSV export.

## Scope Boundaries

- **In scope:** Removing self-registration and its links; adding the unique, required-for-applicants User Code (create/edit/profile read-only); widening search on the three surface groups to also match identification + User Code.
- **Out of scope:** The existing `CodigoPersonal` field (untouched), supplier search, backfilling/bulk-importing codes, any self-service onboarding replacement, and any User Code format validation beyond length + uniqueness.
- **Why these boundaries:** Keep the change a thin, additive slice over existing patterns (LegalId is already required-for-applicants and already searchable in some surfaces); avoid touching unrelated subsystems.

## Critical Decisions

### New field, not a reuse of `CodigoPersonal`
- **Choice:** Add a brand-new User Code distinct from the existing admin-set `CodigoPersonal`.
- **Trade-off:** Two free-text admin codes now exist on a user; potential for confusion vs. preserving `CodigoPersonal`'s current spec-021 meaning untouched.
- **Feedback:** Is keeping both codes acceptable, or should they eventually be reconciled?

### Lives on the applicant record (beside LegalId)
- **Choice:** Store User Code on the applicant, not on the account.
- **Trade-off:** Admin users list (account-based) must join to the applicant to search/show it — but it already needs that join for LegalId, so this is consistent.
- **Feedback:** Confirm applicant-scoped is right (non-applicants never get a code).

### Required + unique for Solicitante
- **Choice:** Block save on blank or duplicate code for applicants; uniqueness enforced only among assigned codes (nullable allows many code-less applicants).
- **Trade-off:** Existing code-less applicants stay valid but invisible to User-Code search until assigned.

## Areas of Potential Disagreement

### Two distinct codes on a user
- **Decision:** New field rather than extending `CodigoPersonal` (40→50).
- **Why this might be controversial:** A future reader may see two near-identical "codes" and wonder which is authoritative.
- **Alternative view:** Reuse/extend `CodigoPersonal`.
- **Seeking input on:** Whether the two-code outcome is intended long-term.

### `/Account/Register` returns 404 (not a redirect)
- **Decision:** Hard 404 on the dead endpoint.
- **Why this might be controversial:** A bookmarked link gives a blunt 404 rather than a friendly redirect to sign-in.
- **Alternative view:** 302 to `/Account/Login`.
- **Seeking input on:** Confirmed already (user chose 404); flagged only for visibility.

## Naming Decisions

| Item | Name | Context |
|------|------|---------|
| Applicant attribute | User Code (es-CR "Código de usuario") | Admin-assigned, ≤50 chars, unique among assigned |
| Suggested property | `UserCode` | On the applicant entity, beside LegalId |
| Distinct from | `CodigoPersonal` | Existing spec-021 account field, left unchanged |

## Open Questions

- [ ] Should the reviewer queue show a visible User Code column, or only match on it? (FR-016 leaves this discretionary.)
- [ ] Long-term: keep both User Code and `CodigoPersonal`, or reconcile later?

## Risk Areas

| Risk | Impact | Mitigation |
|------|--------|------------|
| Uniqueness over a nullable column lets many code-less rows coexist but blocks dup assigned codes | Med | Filtered unique index (assigned values only); es-CR duplicate message; duplicate path covered by E2E (in-memory provider won't enforce the index — same pattern as spec 030's `UX_Processes_Name`) |
| Missing a search surface ("any other screen") | Med | Inventory fixed in spec to three surface groups; planning re-greps for stragglers |
| Removing registration breaks a referenced link/flow | Low | Spec enumerates the two link sites (landing CTA, login page) and preserves login/forgot/reset/must-change |

---
*Share with reviewers before implementation.*
