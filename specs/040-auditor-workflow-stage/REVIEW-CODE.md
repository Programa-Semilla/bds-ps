# Code Review — Auditor Workflow Stage (spec 040)

---

## Code Review Guide (30 minutes)

> This section guides a code reviewer through the implementation changes, focusing on
> high-level questions that need human judgment.
>
> ### Phase 1: Full feature implementation (2026-06-18)

**Changed files:** ~45 across Domain/Application/Infrastructure/Web + dacpac + 3 test
projects. Core new code: `Application.cs` transitions, `AuditWorkflowService`,
`AuditorQueueProjection`, `ChecklistTemplateService`, `AuditController`, `Views/Audit/*`,
`Views/Admin/Checklist*`, the notification recipe, and the 3 new dacpac tables.

### Understanding the changes (8 min)

- Start with [`Application.cs`](../../src/FundingPlatform.Domain/Entities/Application.cs)
  (the `Auditor workflow stage (spec 040)` region): the four gated transitions
  (`SendToAudit`/`ReturnFromAudit`/`ResendToAudit`/`ReleaseForSignature`) are the spine.
  Each **returns the `VersionHistory` row it appends** so the service can anchor the
  notification — note this differs from the legacy pattern where the service constructs the VH.
- Then [`AuditWorkflowService`](../../src/FundingPlatform.Infrastructure/Services/AuditWorkflowService.cs):
  the orchestration. It mixes `IApplicationRepository` (aggregate) and `AppDbContext`
  (checklist-response rows) in one scoped UnitOfWork and uses the two-phase outbox save.
- Question: the keystone decision ([D1](research.md#d1-where-the-auditor-gate-slots-in-the-keystone))
  is that **release returns to `ResponseFinalized`** (not a third "awaiting signature" state),
  disambiguated by `_fundingAgreement is null`. Does reusing `ResponseFinalized` for two
  distinct phases (pre-audit vs post-release signing) read clearly, or would a third state
  have been worth the signing-ceremony churn it was avoiding?

### Key decisions that need your eyes (12 min)

**Reviewer-removal enforced at the controller, not the domain helper** (`FundingAgreementController.cs` `Generate`, relates to [FR-013](spec.md))

`CanUserGenerateFundingAgreement(isAdmin, isReviewerAssigned)` was left unchanged (many
call sites + signing tests depend on it); instead `Generate` requires `Auditor||Admin` and
gates the non-admin path on `PendingAudit` + a complete audit checklist.
- Question: is enforcing the role boundary at the controller (rather than the domain method
  the contract named) an acceptable deviation, or should the domain helper be the single source?

**`CanGenerateFundingAgreement` broadened to accept `PendingAudit`** (`Application.cs`)

So the auditor reuses the existing PDF pipeline. The legacy panel could now show "generate"
for a PendingAudit app to an admin; the controller gate is the real enforcement.
- Question: is broadening the shared gate (vs. a separate auditor-only generate method) the
  right trade-off?

**One-active-per-stage = one active per `AppliesToStage` value** (`ChecklistTemplateService.ActivateAsync`, relates to [FR-002](spec.md))

Activating deactivates other active templates with the *same* `AppliesToStage`; resolution
then prefers stage-specific over `Both`. A `Both` and a `Reviewer` template can both be active.
- Question: does this match the intended "one active per stage" semantics, or did the spec mean
  strictly one template applies per effective stage (which would force deactivating `Both` when a
  stage-specific is activated)?

**Edit deactivates items instead of hard-deleting** (`ChecklistTemplate.DeactivateItems`, relates to [FR-003](spec.md))

Hard-delete would FK-violate the NO ACTION `ApplicationChecklistResponses → ChecklistTemplateItems`
link. Edits therefore accumulate inactive item rows over time.
- Question: is the accumulation of inactive items acceptable, or should unreferenced items be
  hard-deleted and only referenced ones deactivated?

### Areas where I'm less certain (5 min)

- [`AuditorQueueProjection.cs`](../../src/FundingPlatform.Application/Services/AuditorQueueProjection.cs)
  ([FR-006](spec.md)): the inbox row's `EnteredAuditAtUtc` is proxied by `UpdatedAt` (the
  reviewer-queue load doesn't hydrate `VersionHistory`), and **`HasProviderWarning` is hardcoded
  `false`** — the provider warning indicator the spec asks for on the inbox is currently only on
  the detail page. Is the proxy acceptable, and is the missing inbox indicator a blocker?
- [`Views/Audit/Detail.cshtml`](../../src/FundingPlatform.Web/Views/Audit/Detail.cshtml)
  ([FR-007](spec.md)): the auditor detail reuses the `ReviewService` projection (full data is
  available) but renders a focused subset — items + provider compliance badge + declared impacts
  + checklist + download. It does **not** render the application's review history. Is "read access
  equivalent to a reviewer's" satisfied by equivalent *data access* + a focused triage view, or
  must the audit page mirror the full reviewer Review page?
- `ChecklistStage`/`ChecklistResponseStatus` are `TINYINT` columns needing `HasConversion<byte>()`
  in EF — InMemory tests don't exercise this, so a missed conversion only surfaces against real SQL.

### Deviations and risks (5 min)

All deviations are logged in [tasks.md](tasks.md#implementation-deviations) (D-A…D-D). Key ones:

- `FundingAgreementController.Generate` (D-A): reviewer-removal at the controller, not the domain
  helper. Question: acceptable?
- No separate `POST /Audit/{id}/Generate` (D-B): auditor generation reuses the re-gated
  `FundingAgreement/Generate` endpoint, redirecting auditors back to `/Audit/{id}`. Question:
  is the cross-controller redirect acceptable, or should generation be a first-class audit action?
- Auditor group-scoping (D-D) **supersedes spec-021 FR-007** (`SupplierAdmin` was global-scope):
  `NormalizeGroupIdsForRole` now keeps Auditor memberships. Two prior tests were updated to the new
  behavior. Question: does this role-semantics change have downstream impact on any supplier-admin
  surface that still assumes the Auditor role is groupless?
- Risk: `AgreementGeneratedApplicant` was **re-pointed** off generation onto release. If any other
  path still expected the old enqueue site, the applicant "ready to sign" email would not fire.
  Verified by E2E (golden path asserts the applicant ready-to-sign surface after release).
