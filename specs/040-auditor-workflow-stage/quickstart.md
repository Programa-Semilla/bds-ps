# Quickstart: Auditor Workflow Stage

**Feature**: 040-auditor-workflow-stage | **Date**: 2026-06-18

## Run the app

```bash
dotnet run --project src/FundingPlatform.AppHost
```
The AppHost auto-deploys the dacpac (including `07_SeedChecklistTemplates.sql`, the default `Both` checklist). Seed accounts (all `Demo123!`): `applicant@`, `reviewer@`, `demo-admin@`, `auditor@programa-semilla.test`.

## Manual walkthrough (golden path)

1. **Reviewer** (`reviewer@`): open an application that has reached `ResponseFinalized` (applicant has done the per-item response). The review page now shows the **Reviewer checklist** and a **"Send to audit"** button (the old "Generate agreement" is gone). Check all required items → **Send to audit**. App → `PendingAudit`.
2. **Auditor** (`auditor@`): open `/Audit` (inbox scoped to the auditor's groups — `auditor@` and `reviewer@` share the seeded groups, so the app appears) → the application appears. Open it: full reviewer-equivalent read access incl. provider compliance/freshness/warnings. Mark every audit item **compliant** → **Approve** → **Generate PDF** → review it → check **"PDF is correct"** → **Release for signature**. App → `ResponseFinalized` (with agreement); applicant gets the "ready to sign" email.
3. **Applicant** (`applicant@`): the existing signing surface appears unchanged; sign → reviewer verifies → `AgreementExecuted`.

## Return path

At step 2, mark one item **non-compliant** with a reason → **Return to reviewer**. App → `ReturnedFromAudit`; the reviewer gets the return email (captured in smtp4dev). The reviewer opens `/Review/{id}`, sees the reason, reworks, re-completes the checklist, re-sends → back to `PendingAudit`.

## Checklist admin

`demo-admin@` → `/Admin/Checklists`: create/edit templates, set `AppliesToStage` (Reviewer/Auditor/Both), add/reorder text items, mark required, activate. Editing items does not alter responses already recorded on applications.

## Tests

```bash
# Unit — domain transitions, checklist gate, confirm-cleared-on-regenerate
dotnet test tests/FundingPlatform.Tests.Unit

# Integration (real DB) — checklist service, audit workflow, recipient resolver
dotnet test tests/FundingPlatform.Tests.Integration

# E2E (filtered — the delivery bar per CLAUDE.md)
dotnet test tests/FundingPlatform.Tests.E2E --filter "FullyQualifiedName~AuditorWorkflow|FullyQualifiedName~ReviewerSendToAudit|FullyQualifiedName~AuditReturn|FullyQualifiedName~ChecklistTemplateAdmin"
# plus the rewired regression set:
dotnet test tests/FundingPlatform.Tests.E2E --filter "FullyQualifiedName~FundingAgreement|FullyQualifiedName~Signing|FullyQualifiedName~GenerateAgreementQueue"
```

## Mail capture (E2E)

smtp4dev sidecar captures the new `ReturnedToReviewerFromAudit` email and the re-pointed `AgreementGeneratedApplicant` email; seed recipients are allowlisted via the `@programa-semilla.test` default.

## Done-when (maps to spec SC)

- No app reaches signing without `PendingAudit` + auditor approve + PDF confirm (SC-001).
- Send-to-audit blocked until all required reviewer items checked; approve blocked until all required audit items compliant (SC-002).
- Auditor end-to-end works + applicant notified (SC-003).
- Return notifies reviewer with reasons; applicant untouched (SC-004).
- Every checklist response + transition attributable (SC-005).
- Signing ceremony + `AgreementExecuted` unchanged (SC-006).
