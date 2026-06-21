# Implementation Plan: Auditor Workflow Stage

**Branch**: `040-auditor-workflow-stage` | **Date**: 2026-06-18 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `specs/040-auditor-workflow-stage/spec.md`

## Summary

Insert a mandatory **auditor gate** between reviewer completion and the funding agreement reaching the applicant for signature (feedback-3 slice C). Two new `ApplicationState` values — `PendingAudit (7)` and `ReturnedFromAudit (8)` — bracket the existing generate-agreement step. The reviewer's former "Generate agreement" action becomes **"Send to audit"** (gated on a reviewer checklist); PDF generation moves to the **auditor**, who completes an audit checklist, approves, generates, confirms the PDF, and **releases** it — which returns the application to `ResponseFinalized` so the **existing signing ceremony runs unchanged**. Non-compliance returns the application to the reviewer (`ReturnedFromAudit`) with per-item reasons and an email. Checklists are admin-configured per-stage templates (`appliesToStage = Reviewer | Auditor | Both`). Three new tables (`ChecklistTemplates`, `ChecklistTemplateItems`, `ApplicationChecklistResponses`), two new columns on `FundingAgreements` (auditor PDF-correctness confirmation), one new notification event, and a re-pointed "ready to sign" notification. No new managed dependencies.

**Central design decision (D1):** the signing ceremony today operates entirely within `ResponseFinalized` (applicant signed-upload gate and `ExecuteAgreement` both require it). To honor "signing ceremony unchanged" with only two new states, **release-for-signature transitions `PendingAudit → ResponseFinalized`**, with the PDF already generated during audit. "Send to audit" is offered only when the application is in `ResponseFinalized` **and no funding agreement exists yet**; once an agreement exists (post-audit), the same `ResponseFinalized` state shows the unchanged signing surface. A funding agreement is only ever created on the audit-approval path, never on the return path, so the two `ResponseFinalized` phases never overlap.

## Technical Context

**Language/Version**: C# / .NET 10.0, ASP.NET MVC, EF Core 10
**Primary Dependencies**: ASP.NET Identity, .NET Aspire, Syncfusion HtmlToPdfConverter (existing agreement PDF), existing notification outbox (specs 021/028), Tabler.io vendored UI. **No new managed dependency.**
**Storage**: SQL Server via dacpac (`FundingPlatform.Database`), schema-first. EF Core DbContext in Infrastructure.
**Testing**: xUnit (Unit + Integration against real SQL), Playwright E2E via `AspireFixture`.
**Target Platform**: Linux server (Aspire-orchestrated), browser UI (es-CR).
**Project Type**: Web application — Clean Architecture (Domain / Application / Infrastructure / Web).
**Performance Goals**: Standard interactive web latencies; no special throughput target. Auditor inbox is a paged list query.
**Constraints**: es-CR user-facing copy; optimistic concurrency on the `Application` aggregate (existing `RowVersion`); group-overlap scope (spec 016) applies to **both reviewers and auditors** — the auditor inbox + detail page are group-scoped exactly like the reviewer queue + detail page, and auditors are notified group-scoped the same way reviewers are (updated 2026-06-18 per stakeholder; supersedes the earlier global-inbox draft).
**Scale/Scope**: Small auditor population; volume comparable to the existing reviewer queue / generate-agreement queue.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **I. Clean Architecture** — PASS. New domain (`ChecklistTemplate`/`ChecklistTemplateItem`/`ApplicationChecklistResponse` entities, new `Application` state-transition methods, `ChecklistStage`/`ChecklistResponseStatus` enums) lives in Domain. Use cases (`IChecklistTemplateService`, `IAuditWorkflowService`, `IAuditorQueueProjection`, audit-stage projection) in Application. EF config, repositories, service impls, notification recipient rules in Infrastructure. Controllers/views in Web. Dependencies point inward.
- **II. Rich Domain Model** — PASS. New state transitions (`SendToAudit`, `ReturnFromAudit`, `ResendToAudit`, `ReleaseForSignature`) are gated domain methods on `Application`; the PDF-correctness confirmation is a method on `FundingAgreement` (`ConfirmByAuditor` / cleared by `Replace`). Checklist completeness invariants live on the entities, not controllers.
- **III. E2E (NON-NEGOTIABLE)** — PASS. Each user story (US1 auditor end-to-end, US2 reviewer send-to-audit, US3 return path, US4 checklist admin) gets Playwright coverage; existing funding-agreement/signing E2E rewired to route through audit. Delivery bar = filtered E2E green (per CLAUDE.md).
- **IV. Schema-First** — PASS. New tables + the two `FundingAgreements` columns are authored in the `FundingPlatform.Database` dacpac; default checklist seeded via a numbered post-deploy script. New `ApplicationState` ints need no DDL (plain `INT`, no CHECK/lookup — confirmed).
- **V. Specification-Driven** — PASS. Spec approved + soundness-gated; this plan precedes implementation.
- **VI. Simplicity** — PASS. Reuses the existing review projection, outbox, audit-event writer, PDF generation, and signing ceremony. Two new states (not three) by reusing `ResponseFinalized` for the post-release signing phase. Global single-active-per-stage checklists (no per-process scoping). Optimistic concurrency reused (Quality Gate). No speculative abstraction.

**Result: PASS — no violations. Complexity Tracking not required.**

## Project Structure

### Documentation (this feature)

```text
specs/040-auditor-workflow-stage/
├── plan.md              # This file
├── research.md          # Phase 0 — decisions D1–D14
├── data-model.md        # Phase 1 — entities, states, schema, dacpac
├── quickstart.md        # Phase 1 — how to run/verify the slice
├── contracts/
│   └── interfaces.md     # Phase 1 — service interfaces, routes, domain methods, notification events
├── checklists/
│   └── requirements.md   # Spec quality checklist (from /speckit-specify)
├── spec.md / review_brief.md / REVIEW-SPEC.md
└── tasks.md             # Phase 2 — created by /speckit-tasks (NOT here)
```

### Source Code (repository root)

```text
src/
  FundingPlatform.Domain/
    Enums/ApplicationState.cs                 # + PendingAudit=7, ReturnedFromAudit=8
    Enums/ChecklistStage.cs                   # NEW (Reviewer=1, Auditor=2, Both=3)
    Enums/ChecklistResponseStatus.cs          # NEW (Checked=1, NotCompliant=2)
    Entities/Application.cs                    # + SendToAudit/ReturnFromAudit/ResendToAudit/ReleaseForSignature + audit-gen gate
    Entities/FundingAgreement.cs               # + AuditorConfirmedAtUtc/ByUserId + ConfirmByAuditor(); Replace() clears it
    Entities/ChecklistTemplate.cs              # NEW aggregate (mirrors Category)
    Entities/ChecklistTemplateItem.cs          # NEW child (mirrors CategoryField)
    Entities/ApplicationChecklistResponse.cs   # NEW (per-app, stage, item snapshot)
    Entities/AdminAuditEvent.cs                # + checklist.* action constants + TargetTypeChecklist
    Interfaces/IChecklistTemplateRepository.cs # NEW
  FundingPlatform.Application/
    Checklists/IChecklistTemplateService.cs    # NEW admin CRUD (mirrors IFundService)
    Audit/IAuditWorkflowService.cs             # NEW send-to-audit/return/approve/generate/confirm/release
    Audit/IAuditorQueueProjection.cs           # NEW group-scoped PendingAudit inbox (reviewer-scope seam)
    Audit/AuditChecklistDtos.cs                # NEW DTOs (inbox rows, checklist render, responses)
  FundingPlatform.Infrastructure/
    Persistence/Configurations/Checklist*Configuration.cs            # NEW (3 configs)
    Persistence/Repositories/ChecklistTemplateRepository.cs          # NEW
    Services/ChecklistTemplateService.cs                             # NEW (mirrors FundService)
    Services/AuditWorkflowService.cs                                 # NEW
    Audit/AdminAuditEventWriter.cs                                   # + checklist.* prefix routing
    Notifications/Resolvers/NotificationRecipientResolver.cs         # + ReturnedToReviewerFromAudit + new Auditor bucket (group-scoped, role=AUDITOR) for SentToAuditAuditor
  FundingPlatform.Web/
    Controllers/AuditController.cs             # NEW auditor inbox + audit actions (Auditor/Admin), group-scoped (reviewer pattern)
    Controllers/ReviewController.cs            # reviewer "Send to audit" + returned-app rework
    Controllers/Admin/AdminUsersController.cs  # group selector now also shown for Auditor role (FR-017)
    Controllers/FundingAgreementController.cs  # generation gated to audit stage; release enqueues "ready to sign"
    Controllers/AdminController.cs             # + Checklists CRUD actions
    ViewModels/Admin/ChecklistAdminViewModels.cs   # NEW
    Views/Audit/*.cshtml                       # NEW inbox + audit review + checklist
    Views/Admin/Checklists.cshtml + Create/Edit + _ChecklistItems*.cshtml   # NEW
    Views/Emails/ReturnedToReviewerFromAudit*.cshtml                # NEW es-CR templates
    Views/Emails/SentToAuditAuditor*.cshtml                         # NEW es-CR templates (group-scoped auditor notice, FR-018)
    Views/Shared/_Layout.cshtml                # + sidebar entries (Checklists admin; Auditoría inbox)
  FundingPlatform.Database/
    Tables/dbo.ChecklistTemplates.sql / dbo.ChecklistTemplateItems.sql / dbo.ApplicationChecklistResponses.sql   # NEW
    Tables/dbo.FundingAgreements.sql           # + AuditorConfirmedAtUtc/ByUserId columns
    PostDeployment/07_SeedChecklistTemplates.sql   # NEW default template + items (idempotent)
    PostDeployment/SeedData.sql                # + :r 07
    FundingPlatform.Database.sqlproj           # + register 07 (Build Remove / None Include)
tests/
  FundingPlatform.Tests.Unit/                  # domain transitions, checklist gate, confirm-clear-on-regenerate
  FundingPlatform.Tests.Integration/           # checklist service, audit workflow service, recipient resolver (real DB)
  FundingPlatform.Tests.E2E/                   # US1–US4 + rewired FundingAgreement/Signing/GenerateAgreementQueue
    Fixtures/FundingAgreementSeeder.cs         # + SeedPendingAuditApplicationAsync helper
```

**Structure Decision**: Existing Clean-Architecture web app layout (Option 2 shape, already established). All new files slot into the existing four projects + the dacpac + the three test projects; no new project is introduced.

## Complexity Tracking

No constitution violations — section intentionally empty.
