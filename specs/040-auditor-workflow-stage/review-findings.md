# Deep Review Findings

**Date:** 2026-06-18
**Branch:** 040-auditor-workflow-stage
**Rounds:** 1
**Gate Outcome:** PASS
**Invocation:** superpowers (after_implement quality gate)

## Summary

| Severity | Found | Fixed | Remaining |
|----------|-------|-------|-----------|
| Critical | 0 | 0 | 0 |
| Important | 6 | 6 | 0 |
| Minor | 15 | 8 | 7 |
| **Total** | **21** | **14** | **7** |

**Agents completed:** 5/5 (Correctness, Architecture, Security, Production-Readiness, Test-Quality). External tools: skipped (`--no-external`).

## Findings (Important — all fixed)

### FINDING-1 — Stale auditor confirmation leaks across the return/resend loop
- **Severity:** Important · **Confidence:** 78 · **Category:** correctness
- **File:** `src/FundingPlatform.Domain/Entities/Application.cs` (`ReturnFromAudit`)
- **Resolution:** fixed (round 1)

**What was wrong:** After confirm → `ReturnFromAudit` left `FundingAgreement.AuditorConfirmedAtUtc` set, so a later `ReleaseForSignature` (after re-send) succeeded without a fresh confirmation — violating FR-010/SC-001.

**How it was resolved:** `ReturnFromAudit` now calls `FundingAgreement.ClearAuditorConfirmation()`. A new unit test (`ReturnFromAudit_ClearsPriorConfirmation_SoReleaseNeedsFreshConfirm`) proves release fails until a fresh confirm.

### FINDING-2 — Concurrent auditors can create duplicate checklist response rows
- **Severity:** Important · **Confidence:** 72 · **Category:** correctness
- **File:** `src/FundingPlatform.Infrastructure/Services/AuditWorkflowService.cs` (`SaveAuditChecklistAsync`) + dacpac/EF config
- **Resolution:** fixed (round 1)

**What was wrong:** No unique constraint on `(ApplicationId, Stage, ChecklistTemplateItemId)`; concurrent saves could insert duplicates, later breaking `ToDictionary` reads. The `RowVersion` catch could not fire on fresh inserts.

**How it was resolved:** Added `UX_ApplicationChecklistResponses_App_Stage_Item` unique index (dacpac + EF) and a `DbUpdateException` catch returning a clean stale-state refusal. Verified on real SQL (the replace path's delete-before-insert is intact; AuditReturn/AuditorWorkflow E2E save+resave green).

### FINDING-3 — AdminController role breadth lets Auditors administer checklists
- **Severity:** Important · **Confidence:** 80 · **Category:** security
- **File:** `src/FundingPlatform.Web/Controllers/AdminController.cs`
- **Resolution:** fixed (round 1)

**What was wrong:** The class `[Authorize(Roles="Admin,Auditor")]` let an Auditor reach the spec-040 checklist CRUD — i.e. deactivate the very gate they are subject to (privilege escalation vs FR-001).

**How it was resolved:** Added per-action `[Authorize(Roles="Admin")]` to all checklist actions (Checklists/CreateChecklist/EditChecklist/Activate/Deactivate); they AND with the class attribute → Admin-only.

### FINDING-4 — Phase-2 notification failure unhandled/unlogged
- **Severity:** Important · **Confidence:** 72 · **Category:** production-readiness
- **File:** `src/FundingPlatform.Infrastructure/Services/AuditWorkflowService.cs`
- **Resolution:** fixed (round 1)

**What was wrong:** A generic phase-2 (outbox) `DbUpdateException` propagated after the transition had committed, producing a 500 with the email lost and nothing logged — violating FR-011 scenario 5.

**How it was resolved:** New `EnqueueAfterCommitAsync` wraps the enqueue+save in try/catch, logs the failure, and lets the committed transition stand. Applied to send/re-send, release, and return. Also wired the previously-unused `ILogger` across all transitions (addresses the unused-logger Minor).

### FINDING-5 — Approve-gate negative branch never tested (SC-002)
- **Severity:** Important · **Confidence:** 85 · **Category:** test-quality
- **Resolution:** fixed (round 1) — added `Approve_RefusedWhenRequiredItemNotCompliant_StateUnchanged`.

### FINDING-6 — FR-011 return payload + persisted reasons unasserted
- **Severity:** Important · **Confidence:** 78 · **Category:** test-quality
- **Resolution:** fixed (round 1) — `ReturnPath` now asserts the persisted `NotCompliant` reason AND the outbox `PayloadJson` carries the itemized finding.

## Findings (Minor — fixed)

- **Stale auditor responses across the loop** (correctness, conf 70): re-send now clears the prior auditor-stage responses so each cycle starts clean.
- **Inaccurate `AuditorQueueProjection` comment + inbox query efficiency** (architecture/prod, conf 85/85): comment corrected; `GetPendingAuditInboxAsync` now `AsNoTracking().AsSplitQuery()`.
- **Dead `IChecklistTemplateService.GetActiveForStageAsync` + `ActiveChecklist` (duplicate resolution)** (architecture, conf 88): removed; the repository is now the single resolution authority (its rule stays tested).
- **Dead `IChecklistTemplateRepository.GetByIdWithItemsAsync`** (architecture, conf 80): removed.
- **Missing logging on transitions/catches** (prod, conf 80): wired via the logger work above.

## Remaining Findings (Minor — not blocking; recorded for human review)

- **Dead `FundingAgreementService._outboxWriter`** (architecture, conf 92): genuinely dead after the re-point, but removing the ctor param ripples to ~10 test construction sites; left as-is to avoid churn. Safe to remove in a follow-up.
- **`ApproveForAgreementAsync` hand-rolls its `VersionHistory`** (architecture, conf 72) instead of a domain `Application.ApproveForAudit` method — minor abstraction inconsistency vs the other transitions.
- **Heavy `GetByIdWithResponseAndAppealsAsync` include graph on scalar-only actions** (prod, conf 70): Approve/Confirm/Release/Return load the full aggregate graph but read only `State` + `FundingAgreement`. Optimization opportunity.
- **Out-of-group auditor POST-mutation guards untested** (test, conf 80): only the GET detail 403 is tested. Practical exploit is blocked by `[ValidateAntiForgeryToken]` + the GET-403 (no token obtainable), and the underlying `ApplicantSharesAnyGroupAsync` is tested; the per-endpoint `EnsureInScopeAsync` wiring is uniform. Defense-in-depth — a dedicated test would still be valuable.
- **Auditor generate click-path untested** (test, conf 72): E2E SQL-seeds the agreement to bypass Syncfusion (project convention), so the `FundingAgreement/Generate` re-gate is covered by unit/integration but not an E2E click.
- **Reviewer send-to-audit gate assertion is conditional** (test, conf 75): `if (requiredCount > 0)` could silently skip the disabled-state assertion if seed data drifts to all-optional items.
- **SC-006 release→full-signing not chained in E2E** (test, conf 70): the golden path stops at the applicant ready-to-sign banner; the existing signing ceremony is covered by its own (rewired) suite.
- **FR-003 newly-added-required-item-not-retroactive edge untested** (test, conf 70): the preservation test covers edit→snapshot-unchanged but not "add a new required item ⇒ prior response stays complete."
