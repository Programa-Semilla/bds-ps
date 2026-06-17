# Phase 0 Research: Funds-Usage Evidence Stage

All decisions below were grounded by reading the existing seams (paths cited). No
`NEEDS CLARIFICATION` markers remained from the spec; the open threads from
brainstorming are recorded as D1 and D3 with the chosen default + revisit note.

## D1 — Availability trigger = `AgreementExecuted`

- **Decision**: The evidence stage is available iff `Application.State == AgreementExecuted`
  (`ApplicationState.AgreementExecuted = 6`, `src/FundingPlatform.Domain/Enums/ApplicationState.cs`).
- **Rationale**: `AgreementExecuted` is the terminal lifecycle state, reached when a reviewer
  approves the signed funding agreement (`Application.ExecuteAgreement`, `Application.cs:918`).
  It is the closest existing proxy for "funds disbursed". Confirmed with the user during brainstorming.
- **Alternatives**: Gate on `Resolved` (too early — agreement not yet signed); introduce a new
  `Disbursed` state (rejected — no disbursement event exists; adds state-machine machinery for no
  current need). **Open thread (revisit):** if a real disbursement event is ever modeled, move the gate.

## D2 — `FundsUsageEvidence` as its own aggregate, creation gated by a domain factory

- **Decision**: `FundsUsageEvidence` is a standalone aggregate root with an `ApplicationId` FK and
  its own `DbSet` + repository — **not** a navigation collection on the `Application` aggregate.
  The `AgreementExecuted` invariant is enforced by a domain factory
  `FundsUsageEvidence.CreateForExecutedApplication(Application application, …)` that throws if the
  application is not executed; note length is enforced in the ctor / `EditNote`.
- **Rationale**: `Application` is a large aggregate (~1022 lines, many collections). Hydrating it for a
  single evidence add/edit/delete is wasteful. The `SignedUpload` precedent already keeps a
  post-resolution upload in a *separate* small aggregate (`FundingAgreement`) rather than on
  `Application` (`SignedUpload.cs`). Passing the `Application` (a cheap tracked load — `State` is a
  scalar) into the factory keeps the invariant in the domain (Constitution II) while reads stay flat.
- **Alternatives**: Child collection on `Application` (rejected — forces aggregate hydration on every
  write; heavier, no invariant benefit since the only cross-entity rule is the scalar state gate).

## D3 — File-type policy: curated allow-list (ext + content-type + magic-byte family)

- **Decision**: Accept a curated allow-list, validated at the controller boundary by a pure
  `EvidenceFileTypePolicy` (Application layer, unit-testable):
  | Family | Extensions | Declared content-types | Magic-byte sniff |
  |---|---|---|---|
  | PDF | `.pdf` | `application/pdf` | `%PDF-` |
  | PNG | `.png` | `image/png` | `89 50 4E 47` |
  | JPEG | `.jpg`,`.jpeg` | `image/jpeg` | `FF D8 FF` |
  | WebP | `.webp` | `image/webp` | `RIFF`…`WEBP` |
  | HEIC | `.heic`,`.heif` | `image/heic`,`image/heif` | `ftyp` box (`heic`/`heix`/`mif1`) at offset 4 |
  | Word | `.docx`,`.doc` | OOXML wordprocessing / `application/msword` | `PK\x03\x04` (zip) or `D0 CF 11 E0` (OLE) |
  | Excel | `.xlsx`,`.xls` | OOXML spreadsheet / `application/vnd.ms-excel` | `PK\x03\x04` (zip) or `D0 CF 11 E0` (OLE) |
  Rule: extension ∈ allow-list **AND** declared content-type consistent with that extension's family
  **AND** the buffered head matches the family's magic bytes. Otherwise reject with es-CR message,
  create no row, store no blob.
- **Rationale**: Mirrors the existing `%PDF-` magic-byte boundary check in `AdminFundsController.ValidatePdfAsync`
  (`AdminFundsController.cs:226`). The user chose "common evidence types" over genuinely-any.
- **Limitation (noted, accepted)**: `.docx`/`.xlsx` share the zip signature, so magic bytes confirm
  "OOXML zip" but not Word-vs-Excel — acceptable (both are allowed). Deep OOXML part inspection is out of
  scope (no malware scanning per spec). **Open thread (revisit):** allow genuinely-any type if stakeholders require.

## D4 — New `FileCategory.FundsUsageEvidence` + storage category options (purely additive)

- **Decision**: Add `FundsUsageEvidence` to `FileCategory` (`FileCategory.cs:10`) with container
  `funds-usage-evidence`, wire `ContainerName()` + `AllContainerNames`; add a matching
  `StorageCategoryOptions FundsUsageEvidence` (MaxSizeBytes = `DefaultMaxSizeBytes20Mib`,
  `ServingMode.BackendStream`) to `StorageCategoriesOptions` and its `For()` switch (`StorageOptions.cs:62`).
- **Rationale**: `UploadSizeGuardFilter` reads the cap via `StorageOptions.Categories.For(category)`
  (`UploadSizeGuardAttribute.cs:68`), and `StorageOptionsValidator` iterates `Enum.GetValues<FileCategory>()`
  calling `For()` (`StorageOptions.cs:174`) — so the new enum member MUST have a `For()` case or
  validation throws at startup. Both edits are additive; existing categories untouched.
- **Alternatives**: Reuse `ApplicationAttachment` (rejected — that container has its own semantics/retention;
  evidence deserves its own bucket for clarity and future retention policy).

## D5 — Group-scoped, reviewer/admin-only access via the established seam

- **Decision**: `[Authorize(Roles = "Reviewer,Admin")]` on the controller. Per-application gate uses
  `IReviewerScopeProvider.GetForUserAsync(userId, User.IsInRole("Admin"), ct)` then
  `IApplicationRepository.ApplicantSharesAnyGroupAsync(applicationId, scope.GroupIds, ct)` with admin
  short-circuit — the exact pattern `ReviewController` uses (`ReviewController.cs:118,563`). Any failure →
  `NotFound()` (no disclosure). Applicants are excluded by the role gate.
- **Rationale**: Single source of truth for reviewer scoping (spec 016); no new auth logic.

## D6 — Audit via `IAdminAuditWriter` + new `AdminAuditEvent` action keys

- **Decision**: Add action-key constants to `AdminAuditEvent` (`AdminAuditEvent.cs`):
  `funds_evidence.uploaded`, `funds_evidence.note_edited`, `funds_evidence.deleted`, plus
  `TargetTypeFundsEvidence = "funds_evidence"`. Records written via `IAdminAuditWriter.WriteAsync`
  in the same transaction as the mutation (the service stages the row, then one `SaveChanges`).
  `TargetId` = evidence id (or application id for uploads before the id exists — use application id +
  file name in payload). Payload JSON: `{ applicationId, evidenceId, fileName }`.
- **Rationale**: Mirrors `fund.*` / `process.*` audit usage. `AdminAuditEvent.Record(...)` validates
  non-empty actor/action/targetType/targetId (`AdminAuditEvent.cs:89`).
- **Note**: `AdminAuditEventWriter` routes by action prefix to a target; confirm the `funds_evidence.`
  prefix is handled (extend the writer's routing if it switches on prefix) — pin during implementation.

## D7 — Surfacing the stage on the per-application reviewer surface

- **Decision**: Mount the controller at `Applications/{applicationId:int}/Evidence` (mirrors
  `FundingAgreementController`'s `Applications/{applicationId:int}/FundingAgreement`, `FundingAgreementController.cs:28`).
  The stage is reached from the per-application reviewer surface (the funding-agreement / review detail
  area) via a stage card/link shown **only when** `State == AgreementExecuted`. The global `_ReviewTabs.cshtml`
  is queue-level (Initial / Generate / Signing) and is **not** the right place; the evidence link belongs on
  the per-application detail like the funding-agreement panel.
- **Rationale**: Evidence is per-application and post-execution, exactly like the funding-agreement
  surface; reusing that mount point keeps "reach it like the other stages" literally true.
- **Implementation note**: Add a conditional "Evidencia de uso de fondos" entry to the per-application
  reviewer navigation (the funding-agreement Details/panel area) gated on the executed state. Exact
  partial pinned during implementation.

## D8 — E2E seed must reach `AgreementExecuted`

- **Decision**: The E2E suite needs an application in `AgreementExecuted`. Reuse the existing signing-ceremony
  E2E helpers/dev-seams that drive an application through generate → upload signed → approve (the path that
  calls `Application.ExecuteAgreement`). If no single helper exists, add a Development-only dev seam to fast-forward
  an application to `AgreementExecuted` (mirrors existing `SeedUser`/`AssignAllGroups` dev seams) — pin in tasks.
- **Rationale**: Constitution III requires green E2E per story; the gate (FR-001/SC-005) is only testable from an
  executed application.

## D9 — Delete removes blob then row; concurrency via RowVersion

- **Decision**: Delete = load evidence (group-scoped) → `IObjectStorage.DeleteAsync(category, key)` → remove row →
  `SaveChanges`. Concurrent double-delete resolves to not-found on the second (RowVersion / missing row →
  `NotFound()`), surfaced to the user without error (FR-007 edge). Upload writes the blob first, then the row in a
  transaction; if the row write fails, best-effort blob cleanup — no orphaned row can exist (a row is only committed
  after a successful blob write). No reference counting (one blob per evidence item, unlike spec-035 quotation reuse).
- **Rationale**: Matches `SignedUpload` RowVersion concurrency (`SignedUpload.cs:23`) and the storage delete seam.

## D10 — es-CR copy in resources; no English literals in views/JS

- **Decision**: All labels, validation/rejection messages, the confirm-dialog text, and the empty-state live in a
  `FundsUsageEvidenceResources` resource set (Web), consistent with `AdminFundsResources` and the spec-024
  toast/confirm + spec-012 localization conventions. The size-cap rejection reuses
  `UploadSizeGuardFilter.RejectionMessage`.
- **Rationale**: Default culture es-CR; localization is in scope (FR-011).
