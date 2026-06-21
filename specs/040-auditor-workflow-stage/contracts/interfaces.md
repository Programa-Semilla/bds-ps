# Contracts: Auditor Workflow Stage

**Date**: 2026-06-18 | **Feature**: 040-auditor-workflow-stage

UI/service/domain contracts. This is an MVC app — "contracts" are controller routes, Application-layer service interfaces, and domain method signatures. Shapes are indicative; final names settle in tasks.

---

## 1. Domain methods (`Application` / `FundingAgreement`)

```csharp
// Application.cs — new gated transitions (es-CR refusal on guard failure)
void SendToAudit(string reviewerUserId, bool reviewerChecklistComplete);
//   guard: State==ResponseFinalized && !HasOpenAppeal && _fundingAgreement is null && reviewerChecklistComplete
//   => State=PendingAudit; AddVersionHistory("SentToAudit")

void ReturnFromAudit(string auditorUserId);
//   guard: State==PendingAudit
//   => State=ReturnedFromAudit; AddVersionHistory("ReturnedFromAudit")

void ResendToAudit(string reviewerUserId, bool reviewerChecklistComplete);
//   guard: State==ReturnedFromAudit && reviewerChecklistComplete
//   => State=PendingAudit; AddVersionHistory("ResentToAudit")

void ReleaseForSignature(string auditorUserId);
//   guard: State==PendingAudit && _fundingAgreement is not null && _fundingAgreement.AuditorConfirmedAtUtc is not null
//   => State=ResponseFinalized; AddVersionHistory("ReleasedForSignature")

bool CanAuditorGenerateFundingAgreement(out IReadOnlyList<string> errors);
//   State==PendingAudit + audit checklist all-required-compliant + existing accepted-items checks

// CanUserGenerateFundingAgreement(isAdmin, isReviewerAssigned) -> now (isAdmin || isAuditor); reviewer removed

// FundingAgreement.cs
void ConfirmByAuditor(string auditorUserId);   // sets AuditorConfirmedAtUtc/ByUserId
// Replace(...) now also clears AuditorConfirmedAtUtc/ByUserId  (regenerate invalidates confirm)
```

---

## 2. Application-layer services

```csharp
// Checklists/IChecklistTemplateService.cs — admin CRUD (mirrors IFundService)
public interface IChecklistTemplateService
{
    Task<IReadOnlyList<ChecklistTemplateRow>> ListAsync(ChecklistStage? stageFilter, bool? activeFilter, CancellationToken ct);
    Task<ChecklistTemplateDetail?> GetDetailAsync(int id, CancellationToken ct);
    Task<int>  CreateAsync(CreateChecklistTemplateCommand cmd, string actorUserId, CancellationToken ct);
    Task       EditAsync(EditChecklistTemplateCommand cmd, string actorUserId, CancellationToken ct);   // full-replace items
    Task       ActivateAsync(int id, string actorUserId, CancellationToken ct);     // enforces one-active-per-effective-stage
    Task       DeactivateAsync(int id, string actorUserId, CancellationToken ct);
    // resolution helper for gates:
    Task<ActiveChecklist?> GetActiveForStageAsync(ChecklistStage stage, CancellationToken ct); // stage-specific beats Both
}

// Audit/IAuditorQueueProjection.cs — group-scoped PendingAudit inbox (mirrors reviewer queue)
public interface IAuditorQueueProjection
{
    Task<IReadOnlyList<AuditInboxRowDto>> GetInboxAsync(IReviewerScope scope, string? searchTerm, int page, int pageSize, CancellationToken ct);
}
// scope resolved via IReviewerScopeProvider.GetForUserAsync(auditorUserId, isAdmin) — group ids by UserGroupMembership;
// admin short-circuits to all. Empty groups => empty inbox.
// AuditInboxRowDto: ApplicationId, ApplicantDisplayName, PublicCode?, EnteredAuditAtUtc,
//                   HasProviderWarning, WorstComplianceFlag (indicator), ItemCount

// Audit/IAuditWorkflowService.cs — orchestration (Infrastructure impl)
public interface IAuditWorkflowService
{
    // reviewer side
    Task<Result> SubmitReviewerChecklistAndSendToAuditAsync(int appId, IReadOnlyList<ReviewerCheck> checks, string reviewerUserId, CancellationToken ct);
    Task<Result> ResendToAuditAsync(int appId, IReadOnlyList<ReviewerCheck> checks, string reviewerUserId, CancellationToken ct);

    // auditor side
    Task<AuditChecklistView> GetAuditChecklistAsync(int appId, CancellationToken ct);
    Task<Result> SaveAuditChecklistAsync(int appId, IReadOnlyList<AuditMark> marks, string auditorUserId, CancellationToken ct);
    Task<Result> ApproveForAgreementAsync(int appId, string auditorUserId, CancellationToken ct);   // records VersionHistory "AuditApproved"
    Task<Result> ConfirmPdfAsync(int appId, string auditorUserId, CancellationToken ct);            // FundingAgreement.ConfirmByAuditor
    Task<Result> ReleaseForSignatureAsync(int appId, string auditorUserId, CancellationToken ct);   // re-points AgreementGeneratedApplicant enqueue
    Task<Result> ReturnToReviewerAsync(int appId, string auditorUserId, CancellationToken ct);      // enqueues ReturnedToReviewerFromAudit
}
// ReviewerCheck { TemplateItemId, Checked }   AuditMark { TemplateItemId, Compliant, Reason? }
// Result: success | DomainError(Code) -> es-CR via IUserFacingErrorTranslator
```

PDF generation itself stays in the existing `GenerateFundingAgreementCommand` path; `AuditWorkflowService`/controller invokes it under the re-gated authorization (D11).

---

## 3. Web routes

### Auditor (`AuditController`, `[Authorize(Roles="Auditor,Admin")]`)
Group-scoped exactly like the reviewer surfaces: inbox filtered to the auditor's groups; detail page applies `ApplicantSharesAnyGroupAsync` → `Forbid()` (403) on no overlap (admins exempt).
| Method | Route | Purpose |
|---|---|---|
| GET | `/Audit` | Inbox of `PendingAudit` apps **scoped to the auditor's groups** |
| GET | `/Audit/{id}` | Auditor review surface (reviewer-equivalent read) + audit checklist; group-overlap gated |
| POST | `/Audit/{id}/Checklist` | Save audit marks (compliant / non-compliant + reason) |
| POST | `/Audit/{id}/Approve` | Approve for agreement (gate: all required compliant) |
| POST | `/Audit/{id}/Generate` | Generate agreement PDF (delegates to existing generation; gate: PendingAudit + approved) |
| POST | `/Audit/{id}/Confirm` | Check "PDF is correct" → `FundingAgreement.ConfirmByAuditor` |
| POST | `/Audit/{id}/Release` | Release for signature → `ResponseFinalized` + re-pointed notification |
| POST | `/Audit/{id}/Return` | Return to reviewer (≥1 non-compliant) → `ReturnedFromAudit` + email |
| GET | `/Audit/{id}/Download/...` | Download documents/PDFs (reuse existing storage signed-URL path) |

Wrong role → 403; auditor groups don't overlap applicant's → `Forbid()` 403 (reviewer pattern); app missing → 404; mutation on wrong state → es-CR domain refusal (D12).

### Reviewer (`ReviewController`, existing)
| Method | Route | Purpose |
|---|---|---|
| GET | `/Review/{id}` | Existing review detail — now shows **Reviewer checklist** when `State==ResponseFinalized && no agreement`; shows auditor return reasons when `State==ReturnedFromAudit` |
| POST | `/Review/{id}/SendToAudit` | Submit reviewer checklist + send to audit (gate: all required checked) |

The former reviewer/admin "Generate agreement" action is removed from the reviewer surface.

### Admin checklist templates (`AdminController`, `[Authorize(Roles="Admin")]`)
| Method | Route | Purpose |
|---|---|---|
| GET | `/Admin/Checklists` | List templates (filter by stage/active) |
| GET/POST | `/Admin/CreateChecklist` | Create template + ordered items |
| GET/POST | `/Admin/EditChecklist?id=N` | Edit (full-replace items), activate/deactivate |

Sidebar entry added to `procesoEntries` in `_Layout.cshtml`; optional dashboard capability card.

---

## 4. Notifications

| Event | Trigger | Recipients | Template | CTA |
|---|---|---|---|---|
| `AgreementGeneratedApplicant` (14, **re-pointed**) | `ReleaseForSignatureAsync` (was: PDF generation) | Applicant | existing `AgreementGeneratedApplicant` | `/Applications/{id}/FundingAgreement` |
| `ReturnedToReviewerFromAudit` (20, **new**) | `ReturnToReviewerAsync` | Reviewer bucket (applicant stage groups) + Admin; exclude actor/applicant | new es-CR `ReturnedToReviewerFromAudit(.text)` | `/Review/{id}` |
| `SentToAuditAuditor` (21, **new**) | `SendToAudit` / `ResendToAudit` (entry to `PendingAudit`) | **Auditor bucket** (Auditor role ∩ applicant stage groups) + Admin; exclude actor/applicant | new es-CR `SentToAuditAuditor(.text)` | `/Audit/{id}` |

Requires a new **Auditor** `RecipientBucket` + a resolver query mirroring the reviewer group-overlap join with role filter `AUDITOR`. Idempotency anchor = the `VersionHistory` row created by the send/release/return transition. Email-send failure does not roll back the transition.

### Admin — auditor group assignment (FR-017)
The spec-016 multi-select group selector on the admin user-edit form (shown for Reviewer) MUST also render for the **Auditor** role, so auditors can be assigned to groups. Reuses `UserGroupMembership` + the existing user-administration save path; no new entity.

---

## 5. Acceptance mapping (spec → contract)

- US1 → `/Audit` + `/Audit/{id}` + Checklist/Approve/Generate/Confirm/Release; `CanAuditorGenerateFundingAgreement`; re-pointed notification.
- US2 → `/Review/{id}/SendToAudit`; `Application.SendToAudit`; reviewer "Generate" removed.
- US3 → `/Audit/{id}/Return`; `Application.ReturnFromAudit`; `ReturnedToReviewerFromAudit`; `/Review/{id}` shows reasons + `ResendToAudit`.
- US4 → `/Admin/Checklists` CRUD; `IChecklistTemplateService`; snapshot responses (FR-003).
