# Brainstorm: Applicant Companies — controlled company selection on submission

**Date:** 2026-06-17
**Status:** spec-created
**Spec:** specs/037-applicant-companies/

## Problem Framing

Applicants currently free-type the company name (`Application.CompanyName`, NVARCHAR(200), spec 018) when starting a funding application. Free text yields inconsistent, unvalidated company data. The business wants a controlled dropdown sourced from companies that **administrators** assign to each applicant — applies to the Applicant (Solicitante) role only. A new admin-managed Company concept (name-only for now) is introduced, with historical applications preserving the company name as it was at creation even after later edits.

## Approaches Considered

### Data model for historical preservation

**A: Snapshot + reference (chosen)**
- Keep the existing `CompanyName` column as a frozen name snapshot; add a nullable `CompanyId` reference to a new `Company` aggregate.
- Pros: reuses the column that already exists; satisfies "preserve name at time of submission" with zero versioning; nullable reference keeps pre-existing rows valid (greenfield).
- Cons: minor denormalization (a name stored per application).

**B: Pure reference, resolve name live**
- Store only `CompanyId`; render the current company name everywhere.
- Pros: fully normalized.
- Cons: fails the historical-preservation rule — renaming a company would retroactively change every past application. Rejected.

**C: Versioned company / immutable company rows**
- Pros: full history.
- Cons: overkill for a name-only entity; YAGNI. Rejected.

### Company removal semantics

- No-removal / soft-archive / hard-delete-when-unused. Chose **soft archive + unarchive** with a **last-active floor** (can't archive an applicant's last active company). Reversible, history-safe, matches the codebase's Fund-archival lifecycle (spec 029).

### Batch import scope

- Add a required company column vs. defer. Chose **add a required `Nombre de la empresa` column** appended to the spec-034 CSV so bulk-provisioned applicants satisfy the "≥1 company at creation" rule inline.

### Draft editability

- Lock at creation (like `GroupId`) vs. editable while `Draft`. Chose **editable while `Draft`** (like spec 023 quotation edits), with the name snapshot re-copied on each change and frozen at submission.

## Decision

Build spec 037 with: a new admin-managed `Company` aggregate (one applicant → many companies, name only, active/archived), `Application.CompanyId` nullable reference + retained `CompanyName` snapshot, applicant dropdown (single auto-select / multi explicit-choice / zero blocked), admin create-with-≥1 / add / rename / soft-archive-unarchive with last-active floor, a required batch-CSV company column, and server-side ownership/active validation (no cross-applicant disclosure). Greenfield — no backfill. Reuses existing submission/admin/batch/searchable-dropdown/audit/es-CR seams; no new managed dependencies.

20 FRs, 4 prioritized user stories (US1 selection + US2 admin management = P1; US3 history preservation + US4 batch = P2), 8 success criteria. Spec review: SOUND.

## Open Threads

- Admin UI placement for the per-applicant company list (inline on user Edit vs. dedicated `/Admin/Users/{id}/Companies` sub-surface) — deferred to planning as a HOW decision.
- Audit-event naming prefix (likely `company.*`) — confirm during planning to match `fund.*`/`process.*`/`funds_evidence.*` conventions.
- Whether a one-time backfill of existing applicants/applications is wanted in a later spec (currently greenfield; pre-existing applicants can't submit until an admin adds a company).
