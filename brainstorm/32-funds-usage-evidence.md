# Brainstorm: Funds-Usage Evidence Stage

**Date:** 2026-06-16
**Status:** spec-created
**Spec:** specs/036-funds-usage-evidence/

## Problem Framing

The application lifecycle ends at `AgreementExecuted` (signed funding agreement approved → funds effectively disbursed). There is no surface for what happens next: reviewers need to collect proof that the disbursed funds were used correctly. The ask is a new reviewer-facing stage to upload evidence files (photos, PDFs, Office docs), annotate each with a short note, and delete items — reviewers-and-up only, for now.

## Approaches Considered

### Data model

#### A: Dedicated `FundsUsageEvidence` entity (chosen)
- One row per evidence item (ApplicationId, BlobKey, OriginalFileName, FileSize, ContentType, Note ≤250, UploadedByUserId, UploadedAt) + new `FileCategory.FundsUsageEvidence`.
- Pros: clean aggregate boundary; carries note + uploader metadata natively; easy to audit; easy to expose to applicants later. Mirrors `SignedUpload`/`Document`.
- Cons: one new table.

#### B: Reuse the generic `Document` entity with a discriminator
- Pros: no new table.
- Cons: `Document` has no note/uploader/application link — bolting them on pollutes a shared entity. More risk than a table saves.

#### C: Attach evidence to the `FundingAgreement` aggregate
- Pros: co-located with the post-execution surface.
- Cons: conflates the signed agreement with ongoing funds-usage evidence — different lifecycles/purposes.

### Lifecycle shape
- **Open evidence area (chosen):** no new `ApplicationState`; evidence accrues while the app stays `AgreementExecuted`.
- **New gated state** (e.g. `Closed`): rejected — no current need to "complete" evidence; adds state-machine machinery.

## Decision

Approach **A** (dedicated entity) + **open evidence area**. Reuse the spec-014 storage seam (new `FileCategory`, `UploadSizeGuard`, `BackendStream` download), spec-016 reviewer group-scoping, spec-024 toast/confirm for delete, and the `AdminAuditEvent` audit system. No new managed dependencies, no new lifecycle state.

Settled parameters:
- **Trigger:** `AgreementExecuted`.
- **Access:** Reviewer + Admin, group-scoped (admin sees all); applicants excluded for now.
- **Files:** images (jpg/png/webp/heic), PDF, Office (Word/Excel); 20 MiB/file cap; multiple files; no max count.
- **Note:** optional, ≤250 chars, editable after upload.
- **Delete:** any in-scope reviewer/admin can delete any item, with confirm; removes blob + row.
- **Audit:** upload / note-edit / delete recorded.
- **Display:** file name, note, uploaded-by, uploaded-at, download link.

Spec reviewed SOUND (`REVIEW-SPEC.md`); reviewer brief at `review_brief.md`; technical decisions in `implementation-notes.md`.

## Open Threads

- Confirm `AgreementExecuted` is the right proxy for "funds disbursed" vs. a dedicated disbursement event (from #32).
- Curated file-type allow-list vs. genuinely-any type (raw ask said "all types") — confirm with stakeholders (from #32).
- Applicant visibility of evidence — deferred this iteration; revisit (from #32).
- dacpac ordering for the new `dbo.FundsUsageEvidence` table (+ FK to `Applications`) — greenfield add, no backfill; confirm in plan (from #32).
- Audit-event verb names (`funds_evidence.uploaded` / `.note_edited` / `.deleted`) and es-CR rejection copy — pin during planning (from #32).
- Whether a per-application storage quota / max evidence count is ever needed (currently unbounded, 20 MiB/file only) — parked (from #32).
