# Data Model: Auditor Workflow Stage

**Date**: 2026-06-18 | **Feature**: 040-auditor-workflow-stage

References: research.md (D1–D14). Schema is dacpac-first (`FundingPlatform.Database`); EF configs mirror it.

---

## 1. Enums (Domain)

### ApplicationState (extended)
```
Draft=0, Submitted=1, UnderReview=2, Resolved=3, AppealOpen=4,
ResponseFinalized=5, AgreementExecuted=6,
PendingAudit=7,        // NEW — in the auditor inbox awaiting audit
ReturnedFromAudit=8    // NEW — bounced back to the reviewer with findings
```
Stored as `INT` (no CHECK/lookup). No dacpac change.

### ChecklistStage (NEW)
```
Reviewer=1, Auditor=2, Both=3
```
Used by `ChecklistTemplate.AppliesToStage` (Reviewer/Auditor/Both) and `ApplicationChecklistResponse.Stage` (Reviewer/Auditor only — never Both).

### ChecklistResponseStatus (NEW)
```
Checked=1,        // reviewer checked, or auditor marked compliant
NotCompliant=2    // auditor marked non-compliant (requires reason)
```

---

## 2. New entities

### ChecklistTemplate (aggregate root — mirrors `Category`)
| Field | Type | Notes |
|---|---|---|
| Id | int IDENTITY | PK |
| Name | nvarchar(200) | required; unique among active |
| Description | nvarchar(500) | nullable |
| AppliesToStage | tinyint (ChecklistStage) | Reviewer\|Auditor\|Both |
| IsActive | bit | at most one active per effective stage (admin-service guard) |
| CreatedAtUtc | datetime2 | |
| CreatedByUserId | nvarchar(450) | |
| RowVersion | rowversion | optimistic concurrency |
| _items_ | → ChecklistTemplateItem (1..*) | ordered children |

Behavior: `Update(name, description, stage)`, `Activate()`/`Deactivate()`, `ClearItems()` + `AddItem(text, order, isRequired)` (full-replace on edit, Category pattern).

### ChecklistTemplateItem (child — mirrors `CategoryField`)
| Field | Type | Notes |
|---|---|---|
| Id | int IDENTITY | PK |
| ChecklistTemplateId | int | FK → ChecklistTemplates (cascade within aggregate) |
| Text | nvarchar(500) | required; the verification line |
| DisplayOrder | int | ordering within template |
| IsRequired | bit | gate counts required items only |
| IsActive | bit | inactive items not shown/required |

### ApplicationChecklistResponse (NEW — per application, per stage, per item)
| Field | Type | Notes |
|---|---|---|
| Id | int IDENTITY | PK |
| ApplicationId | int | FK → Applications, NO ACTION |
| Stage | tinyint (ChecklistStage) | Reviewer or Auditor (not Both) |
| ChecklistTemplateItemId | int | FK → ChecklistTemplateItems, **NO ACTION** (items never hard-deleted) |
| ItemTextSnapshot | nvarchar(500) | **frozen** text at completion (FR-003) |
| Status | tinyint (ChecklistResponseStatus) | Checked / NotCompliant |
| NonComplianceReason | nvarchar(1000) | required iff Status==NotCompliant |
| CompletedByUserId | nvarchar(450) | who |
| CompletedAtUtc | datetime2 | when |
| RowVersion | rowversion | |

Uniqueness: one current row per `(ApplicationId, Stage, ChecklistTemplateItemId)` — overwritten each completion cycle (research D6); cross-cycle audit lives in `VersionHistory`.

---

## 3. Modified entities

### Application (`Application.cs`)
- **New state transitions** (gated domain methods, research D3): `SendToAudit`, `ReturnFromAudit`, `ResendToAudit`, `ReleaseForSignature`.
- **New gate**: `CanAuditorGenerateFundingAgreement(out errors)` (State==PendingAudit + audit checklist compliant + existing accepted-items checks).
- **Changed gate**: `CanUserGenerateFundingAgreement(isAdmin, isReviewerAssigned)` → now "Auditor or Admin" (reviewer loses direct generate). The existing `CanGenerateFundingAgreement` state precondition shifts from `ResponseFinalized` to `PendingAudit` for the auditor path.
- Each transition appends a `VersionHistory` entry (existing audit trail + notification anchor).

### FundingAgreement (`FundingAgreement.cs`)
| New field | Type | Notes |
|---|---|---|
| AuditorConfirmedAtUtc | datetime2 NULL | set by `ConfirmByAuditor(userId)` |
| AuditorConfirmedByUserId | nvarchar(450) NULL | who confirmed |

`ConfirmByAuditor(userId)` sets the pair; `Replace()` (regenerate) **clears** both (regenerate invalidates a prior confirmation — edge case). `ReleaseForSignature` requires `AuditorConfirmedAtUtc != null`.

### AdminAuditEvent (`AdminAuditEvent.cs`)
Add `checklist.*` constants + `TargetTypeChecklist` (mirror spec-037 `company.*`):
`checklist.create`, `checklist.edit`, `checklist.activate`, `checklist.deactivate`. Routed by a `checklist.` branch in `AdminAuditEventWriter.DeriveTarget`.

### NotificationEvent (`NotificationEvent.cs`)
Add `ReturnedToReviewerFromAudit = 20` (+ storage string `RETURNED_TO_REVIEWER_FROM_AUDIT` in `ToStorageString`/`FromStorageString`). `AgreementGeneratedApplicant = 14` is **re-pointed** (enqueue site moves) — no enum change.

---

## 4. State machine (delta)

```
ResponseFinalized (no agreement yet)
   │  reviewer completes Reviewer checklist
   │  Application.SendToAudit(reviewer)                         ── + ReturnedToReviewer? no; just VersionHistory
   ▼
PendingAudit
   ├─ auditor completes Auditor checklist (all required Checked/compliant)
   │     Application.CanAuditorGenerateFundingAgreement == true
   │     auditor generates PDF (FundingAgreementController, re-gated)
   │     auditor FundingAgreement.ConfirmByAuditor(auditor)
   │     Application.ReleaseForSignature(auditor) ─────────────► ResponseFinalized (agreement exists)
   │           └─ enqueue AgreementGeneratedApplicant (re-pointed) → applicant "ready to sign"
   │           └─ existing signing ceremony UNCHANGED → AgreementExecuted
   │
   └─ auditor marks ≥1 item NotCompliant (+ reason)
         Application.ReturnFromAudit(auditor) ─────────────────► ReturnedFromAudit
               └─ enqueue ReturnedToReviewerFromAudit → reviewer/group

ReturnedFromAudit
   │  reviewer sees reasons, reworks, re-completes Reviewer checklist
   │  Application.ResendToAudit(reviewer) ────────────────────► PendingAudit   (loop any number of times)
```

Unchanged downstream: `SubmitSignedUpload` (gate `ResponseFinalized`), `ApproveSignedUpload` → `ExecuteAgreement` → `AgreementExecuted`.

---

## 5. dacpac changes (`FundingPlatform.Database`)

- `Tables/dbo.ChecklistTemplates.sql`, `Tables/dbo.ChecklistTemplateItems.sql`, `Tables/dbo.ApplicationChecklistResponses.sql` — new (mirror `dbo.FundsUsageEvidence.sql`: IDENTITY PK, FK NO ACTION, `RowVersion`, CK `NonComplianceReason` non-empty when status=NotCompliant is enforced in domain — DB keeps it nullable; NC indexes on FK columns).
- `Tables/dbo.FundingAgreements.sql` — add `AuditorConfirmedAtUtc DATETIME2 NULL`, `AuditorConfirmedByUserId NVARCHAR(450) NULL`.
- `PostDeployment/07_SeedChecklistTemplates.sql` — idempotent default `Both` template + es-CR items; registered in `.sqlproj` + `:r` in `SeedData.sql`.

No CHECK constraint on `Applications.State`; no migration for the new enum ints.

---

## 6. EF configuration (`Infrastructure/Persistence/Configurations`)

- `ChecklistTemplateConfiguration`, `ChecklistTemplateItemConfiguration`, `ApplicationChecklistResponseConfiguration` (mirror `CategoryConfiguration`/`CategoryFieldConfiguration`): table mapping, lengths, `HasMany(items).WithOne()` cascade within the template aggregate, FK to `Applications`/`ChecklistTemplateItems` as `NO ACTION`/`Restrict`, `IsRowVersion()`.
- `FundingAgreementConfiguration` — map the two new nullable columns.
- Register the three new entities/DbSets on `AppDbContext`.

---

## 7. Validation rules (from spec FRs)

- **FR-002**: at most one active template per effective stage — enforced in `ChecklistTemplateService` (pre-check) before activate.
- **FR-003**: editing items never mutates existing `ApplicationChecklistResponse` rows (snapshot + FK NO ACTION).
- **FR-005/FR-009**: gate evaluated against **active required** items of the applicable template; `SendToAudit`/generate refused otherwise (es-CR).
- **FR-008**: `NotCompliant` requires a non-empty reason (domain guard).
- **FR-010**: release refused unless `AuditorConfirmedAtUtc` set; regenerate clears it.
- **FR-013**: generate/confirm/release/return restricted to Auditor or Admin; reviewer cannot generate.
- **Edge — empty/all-inactive stage template**: zero required items ⇒ gate passes immediately (degenerate). Seeded default prevents it in practice.
