# Brainstorm: Admin-only user provisioning + unique applicant User Code

**Date:** 2026-06-11
**Status:** spec-created
**Spec:** specs/032-admin-user-code/

## Problem Framing

Anyone could self-register at `/Account/Register` and was auto-assigned the Applicant role. The organization wants account creation locked to administrators, and wants each applicant tagged with an admin-assigned identifier ("User Code") that becomes a first-class search key — alongside name, email, and the applicant's personal identification (cédula/LegalId) — wherever people are searched in the system.

Recon surfaced two pivotal facts that shaped the spec:
1. There is **already** an admin-set free-text code on the account: `ApplicationUser.CodigoPersonal` (spec 021, NVARCHAR(40), shown on admin form + reports, read-only to the user). The requested "User Code" is semantically near-identical.
2. "Personal identification" is the applicant's `LegalId` + `IdentificationType` (spec 026), stored on the **Applicant** entity, not on the account. Search surfaces are inconsistent today: Admin Users searches only Email/First/Last; the reviewer queue searches First/Last/**LegalId**; the three reports search FullName/**LegalId**/Email. None search any code.

## Approaches Considered

### A: Reuse / extend `CodigoPersonal`
- Pros: one code concept; no duplicate near-identical field; less schema churn.
- Cons: changes the meaning/length (40→50) of an existing spec-021 field; couples two concerns.

### B: New, separate User Code field on the Applicant (chosen)
- Pros: leaves `CodigoPersonal` untouched; applicant-scoped (sits beside LegalId, where it's searched/shown); clean required-for-Solicitante + unique semantics.
- Cons: a user can now carry two free-text codes — possible future confusion (flagged as an open thread + in review_brief).

### Registration removal: 404 vs redirect-to-login
- Chose **404** (user decision) over a 302 to `/Account/Login`; blunt for bookmarks but unambiguous that the capability is gone.

## Decision

Ship as spec **032-admin-user-code**:
- Remove public self-registration; `/Account/Register` (GET/POST) → **404**; strip register links from the home hero CTA (→ login) and the login page. Admin create remains the sole path.
- Add a **new** `UserCode` on the **Applicant**: free text, ≤50 chars, nullable; **required** when role = Solicitante; **unique** among assigned values (filtered unique index tolerates many code-less applicants). Shown/edited on admin Create/Edit only for Solicitante; read-only on the applicant profile.
- Widen the single search box to also match **identification + User Code** on: Admin Users list, Reviewer queue (+ QueueRows), and Admin reports (Applications/Applicants/Aging + applicants CSV). Case- and accent-insensitive; empty term unchanged. Surface the code as a minimal es-CR column where useful.

Spec review: **SOUND** (REVIEW-SPEC.md), no critical/important issues. Constitution-aligned.

## Open Threads

- Long-term reconciliation of the two codes (`UserCode` vs `CodigoPersonal`) — keep both or merge later? (relates to #25's open thread on per-applicant reviewer-assigned code, which assumed reusing `CodigoPersonal`).
- Storage placement confirmation: `UserCode` on the Applicant vs the account — pin in plan (admin users list must join Applicant either way, as it already does for LegalId).
- Filtered unique index over a nullable column + es-CR duplicate-message path — duplicate path is E2E-only (in-memory provider won't enforce the index; mirrors spec 030's `UX_Processes_Name`); pin in plan.
- Reviewer queue: visible User Code column vs match-only (FR-016 left discretionary) — decide in plan, keep minimal.
- Re-grep for any additional people-search surface beyond the fixed three groups during planning ("any other screen" guard).
