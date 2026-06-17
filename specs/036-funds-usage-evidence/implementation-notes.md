# Implementation Notes: Funds-Usage Evidence Stage

Technical context captured during brainstorming. The spec (`spec.md`) stays WHAT/WHY;
this file records the HOW-leaning decisions and the reuse seams so the plan phase has them.

## Design Decisions

### Decision: Open evidence area, not a new lifecycle state
- The evidence stage opens when `Application.State == AgreementExecuted` and stays an
  open collection. **No new `ApplicationState` enum value.**
- Rationale: funds-usage evidence accrues over time as money is spent; it is not a gated
  transition with a single "done" moment. Avoids touching the spec 006/028 signing state
  machine. (User confirmed: trigger = `AgreementExecuted`, shape = open area.)
- Rejected: adding `EvidenceSubmitted`/`Closed` states with a complete-transition — more
  machinery, no current requirement to "close" an application on evidence.

### Decision: Dedicated `FundsUsageEvidence` entity (Approach A)
- One row per evidence item: `Id`, `ApplicationId` (FK), `BlobKey`, `OriginalFileName`,
  `FileSize`, `ContentType`, `Note NVARCHAR(250) NULL`, `UploadedByUserId`, `UploadedAt`.
- Mirrors the `SignedUpload` / `Document` shape already in the domain.
- Rejected Approach B (reuse generic `Document` with a discriminator): `Document` has no
  note/uploader/application link; bolting them on pollutes a shared entity. New table is
  cleaner than the shared-entity risk.
- Rejected Approach C (attach to the `FundingAgreement` aggregate): conflates the signed
  agreement with ongoing funds-usage evidence — different lifecycles and purposes.

### Decision: Reuse the spec-014 storage seam
- New `FileCategory.FundsUsageEvidence` (container e.g. `funds-usage-evidence`), wired into
  `FileCategoryExtensions.ContainerName` + `AllContainerNames`.
- Upload via `IObjectStorage.UploadAsync`; download via `ResolveServingHandleAsync` with
  `ServingMode.BackendStream` (same pattern as `FundRegulationController`).
- Per-file cap 20 MiB enforced at the controller boundary via `UploadSizeGuard` +
  `Storage:Categories:funds-usage-evidence:MaxSizeBytes` config.
- Allowed types validated at the boundary: images (jpg/png/webp/heic), PDF, Office
  (Word/Excel). Reject others with an es-CR message; create no DB row, store no blob.
- Delete removes the blob via `IObjectStorage.DeleteAsync` then the row. (No reference
  counting needed — one blob per evidence item, unlike spec 035 quotation reuse.)

### Decision: Reviewer-scoped controller surfaced as a stage/tab
- New reviewer-only controller (e.g. `FundsUsageEvidenceController`), `[Authorize(Roles = "Reviewer,Admin")]`,
  with group-scoping mirroring `ReviewController` / `FundingAgreementController` (EF-level
  group-overlap predicate from spec 016; admin sees all).
- Actions: List (stage view), Upload (multi-file), EditNote, Delete (confirm via spec-024
  toast/confirm), Download. Out-of-scope/non-existent → `NotFound()` (no disclosure).
- Surfaced as a stage entry alongside the existing review/funding-agreement tabs
  (`_ReviewTabs.cshtml` family), shown only when `State == AgreementExecuted`.

### Decision: Audit via the existing `AdminAuditEvent` system
- Add audit events for upload / note-edit / delete (e.g. `funds_evidence.uploaded`,
  `funds_evidence.note_edited`, `funds_evidence.deleted`) with payload
  `{ applicationId, evidenceId, fileName }`. Same pattern as `fund.*` / `process.*`.

## Constraints / Reuse Inventory
- No new managed (NuGet) dependencies.
- Schema change: one new table `dbo.FundsUsageEvidence` (+ FK to `Applications`). Greenfield
  add — migration-safe (new table, nullable note); follow the dacpac source-of-truth workflow.
- es-CR copy throughout (default culture).

## Open Questions
- None blocking. Exact storage-config defaults (URL expiry not used since BackendStream;
  retention policy `none`) follow the existing per-category convention.
