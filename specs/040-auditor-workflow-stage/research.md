# Research: Auditor Workflow Stage

**Date**: 2026-06-18 | **Feature**: 040-auditor-workflow-stage

Grounded in a five-agent codebase sweep of the application state machine, notification subsystem, admin-CRUD/audit conventions, reviewer-scope/auth surfaces, and dacpac/E2E seeding. Every decision cites the existing code it mirrors.

---

## D1 — Where the auditor gate slots in (the keystone)

**Decision:** Insert two states, `PendingAudit (7)` and `ReturnedFromAudit (8)`, bracketing the existing generate-agreement step. **"Release for signature" transitions `PendingAudit → ResponseFinalized`**; the signing ceremony then runs literally unchanged.

**Rationale:** The signing ceremony today lives entirely inside `ResponseFinalized`: `Application.SubmitSignedUpload` guards `State == ResponseFinalized` (`Application.cs:990`), and `ExecuteAgreement` requires `ResponseFinalized → AgreementExecuted` (`Application.cs:961`). Adding a *third* "awaiting signature" state would force changing those guards — a change to the signing ceremony, which the spec forbids. Returning to `ResponseFinalized` on release keeps the ceremony untouched (SC-006) and holds the new-state count at two (spec requirement).

**Disambiguating the two `ResponseFinalized` phases:** "Send to audit" is offered only when `State == ResponseFinalized && _fundingAgreement is null` (pre-audit). A `FundingAgreement` is created **only on the audit-approval path** (never on return), so once one exists the application is in the post-release signing phase and the same state renders the unchanged signing surface. No overlap.

**Alternatives rejected:**
- Third state `AwaitingSignature` — cleaner state graph but mutates the signing-ceremony guards + breaks signing E2E; violates "signing unchanged" and the two-state constraint.
- Audit before the applicant per-item response — impossible; the agreement PDF is built from accepted items, which only exist post-`ResponseFinalized` (`CanGenerateFundingAgreement` requires it, `Application.cs:790`).

---

## D2 — New state integers and persistence

**Decision:** `PendingAudit = 7`, `ReturnedFromAudit = 8` appended to `ApplicationState` (`Application.cs` enum currently ends at `AgreementExecuted = 6`). No dacpac DDL.

**Rationale:** `Applications.State` is a plain `INT NOT NULL DEFAULT(0)` (`dbo.Applications.sql:19`) with **no CHECK constraint or lookup table** (grep-confirmed; only `CK_Applications_PublicCode` exists). EF stores the enum as int via the default converter (`ApplicationConfiguration.cs:40`). Adding enum values needs only the C# change. The existing `IX_Applications_State` index covers the new inbox query.

---

## D3 — Domain transition methods (Rich Domain Model)

**Decision:** Add gated methods on `Application`, mirroring `Finalize`/`SubmitResponse`/`ExecuteAgreement`:

| Method | Guard (precondition) | Target state |
|---|---|---|
| `SendToAudit(reviewerUserId)` | `State == ResponseFinalized` && no open appeal && `_fundingAgreement is null` && reviewer checklist complete | `PendingAudit` |
| `ReturnFromAudit(auditorUserId)` | `State == PendingAudit` | `ReturnedFromAudit` |
| `ResendToAudit(reviewerUserId)` | `State == ReturnedFromAudit` && reviewer checklist complete | `PendingAudit` |
| `ReleaseForSignature(auditorUserId)` | `State == PendingAudit` && `_fundingAgreement` exists && `AuditorConfirmedAtUtc` set | `ResponseFinalized` |

Generation gate `CanAuditorGenerateFundingAgreement(out errors)` = `State == PendingAudit` && audit checklist all-required-compliant (composing the existing accepted-items checks from `CanGenerateFundingAgreement`). Each transition adds a `VersionHistory` entry (the existing application audit trail, `Application.AddVersionHistory`), which also anchors notifications (D9).

**Rationale:** Matches the existing transition style and Constitution II. Checklist-completeness is passed in (evaluated by the Application service against live template items) rather than the aggregate loading templates — keeps the aggregate boundary clean; the service supplies a boolean/owned check.

---

## D4 — Checklist scope = per-stage templates (resolves §28.9)

**Decision:** `ChecklistTemplate.AppliesToStage : ChecklistStage { Reviewer=1, Auditor=2, Both=3 }`. **One active template applies per stage, global** (not Fund/Process-scoped). A `Both` template satisfies both gates. Seed one default `Both` template.

**Rationale:** The spec resolved §28.9 to per-stage templates; the §22.9 data model anticipates `appliesToStage`. Global single-active mirrors how the gate must resolve deterministically without per-process config (Constitution VI). "Active template for stage X" = the active template whose `AppliesToStage` is `X` or `Both` (if both a stage-specific and a `Both` template are active, the stage-specific wins; enforce at most one active per effective stage in the admin service to avoid ambiguity).

---

## D5 — Checklist template admin (mirror FundService + Category template-with-items)

**Decision:** `IChecklistTemplateService` (Application) + `ChecklistTemplateService` (Infrastructure `Services/`) mirroring `IFundService`/`FundService` (`FundService.cs`). CRUD: list, get-detail, create, edit (name/description/stage/active + full-replace ordered items), activate/deactivate. Ordered text-only items mirror `CategoryField` (`SortOrder`, `IsRequired`, `IsActive`); edit clears + re-adds the item set (`Category.ClearFields()` pattern). Admin surface on `AdminController` with `Checklists`/`CreateChecklist`/`EditChecklist` actions + `_ChecklistItemsEditor`/`_ChecklistItemsScript` partials (mirror `_CategoryFieldsEditor`). Sidebar entry in `procesoEntries` (`_Layout.cshtml:66`).

**Rationale:** Category templates (spec 035) are the closest template→ordered-items analogue; copying them yields consistent CRUD, error handling, and UI with minimal novelty.

---

## D6 — Checklist response model = frozen snapshot (FR-003)

**Decision:** `ApplicationChecklistResponse` rows carry `ApplicationId`, `Stage (Reviewer|Auditor)`, `ChecklistTemplateItemId` (FK NO ACTION), a **frozen `ItemTextSnapshot`**, `Status (Checked|NotCompliant)`, `NonComplianceReason?`, `CompletedByUserId`, `CompletedAtUtc`. The gate (all required complete) is evaluated against the template's **current active required items** at the moment of completion; the recorded rows snapshot the outcome.

**Rationale:** Snapshotting item text (the spec-037 `CompanyName` frozen-snapshot pattern) makes responses immune to later template edits (FR-003) and prevents newly added required items from retroactively applying to an already-submitted response (edge case). FK NO ACTION + no cascade preserves history if an item is later deactivated/removed.

**Re-send loop:** each completion (reviewer send-to-audit, auditor decision, reviewer re-send) overwrites the current per-`(app, stage, item)` rows for that stage; the transition-level audit trail (who/when each cycle) lives in `VersionHistory` (D3). Append-per-cycle history was rejected as heavier than needed given `VersionHistory` already records each transition (Constitution VI).

---

## D7 — Auditor inbox = group-scoped, mirrors the reviewer queue (UPDATED 2026-06-18)

**Decision:** `IAuditorQueueProjection` (Application) + Infrastructure impl querying `Application` rows in `PendingAudit`, using the existing `IApplicationRepository.GetByStateForReviewerAsync` path with the **auditor's own `ReviewerScopeHint`** (their `UserGroupMembership` group ids; admins short-circuit to all) → **group-scoped exactly like the reviewer queue**. Inbox row DTO mirrors `SigningInboxRowDto`: applicant display name, identifiers, time-entered-audit, provider warning/compliance indicators. `ReturnedFromAudit` apps are **excluded** from the auditor inbox (they sit with the reviewer).

**Rationale:** Stakeholder decision (2026-06-18): auditor scope mirrors reviewer scope — auditors are assigned to groups and see only applications whose applicant shares one of their groups (spec 016 group overlap). The `IReviewerScopeProvider.GetForUserAsync(userId, isAdmin)` seam already resolves group ids by `UserGroupMembership` **regardless of role**, so it works for auditors unchanged; pass `isAdmin` for the admin short-circuit. An auditor with no group memberships sees an empty inbox (same as a reviewer). "Time entered audit" comes from the latest `VersionHistory` send-to-audit entry.

**Note (admin user form):** auditors must be assignable to groups. The spec-016 multi-select group selector on the admin user-edit form (currently shown for reviewers) must also be shown for the **Auditor** role (FR-017). Verify the form's role-conditional rendering and extend it to Auditor.

---

## D8 — Auditor read access = reuse the reviewer review projection

**Decision:** The auditor's read surface reuses `ReviewService` projection (`GetApplicationForReviewAsync` / `MapToReviewDto`, `ReviewService.cs:78/274`), which already assembles items, quotations, **provider `SupplierComplianceSnapshot` (regulatory statuses + freshness + warnings, slice A)**, impact/category data, documents, and history, and already renders via `_SupplierComplianceBadge.cshtml`. A new `AuditController` hosts the auditor view with auditor authorization; the view reuses the review partials read-only.

**Rationale:** "Equivalent to reviewer access" (§18.2) is satisfied verbatim by the existing projection. Note: `GetApplicationForReviewAsync` auto-transitions `Submitted → UnderReview`; that branch never fires for a `PendingAudit` app, so reuse is read-safe. A thin read-only projection variant (no auto-transition) may be extracted if cleaner — decided at task time.

**Authorization (UPDATED 2026-06-18):** the auditor detail page applies the **same group-overlap guard as the reviewer detail page** — `IApplicationRepository.ApplicantSharesAnyGroupAsync(appId, auditorGroupIds, ct)` → `Forbid()` (403) when the auditor's groups don't overlap the applicant's (admins exempt). This is the `ReviewController.Review` pattern, not the global 404-no-disclosure of `FundsUsageEvidenceController`.

---

## D9 — Workflow audit trail = VersionHistory; checklist-admin audit = AdminAuditEvent

**Decision:** Workflow transitions (send-to-audit, approve, generate, confirm/release, return) record `VersionHistory` entries on the application (the existing per-application audit trail + notification anchor). Checklist **template administration** mutations (create/edit/activate/deactivate) write `AdminAuditEvent` rows under a new `checklist.*` prefix (constants in `AdminAuditEvent.cs` + a `checklist.` branch in `AdminAuditEventWriter.DeriveTarget`, mirroring the spec-037 `company.*` group). Checklist **responses** capture who/when on the `ApplicationChecklistResponse` rows themselves (D6).

**Rationale:** FR-014 = "consistent with existing administrative/audit event logging." The codebase already splits these: application-state transitions use `VersionHistory`; admin config mutations use `AdminAuditEvent`. Following that split keeps each audit channel coherent.

---

## D10 — Notifications: one new event + one re-point

**Decision:**
1. **New** `NotificationEvent.ReturnedToReviewerFromAudit (20)` — recipients: reviewer bucket (resolved via the applicant's stage groups, spec 016) + admin bucket; applicant excluded; actor (auditor) excluded. Add via the 6-file recipe: enum + `ToStorageString`/`FromStorageString` + `NotificationTemplateBindings` (CTA `"/Review/{id}"`) + `NotificationRecipientResolver` bucket rules + es-CR Razor templates (copy `ResponseSubmittedReviewer.cshtml`/`.text`) + enqueue at the return transition. Idempotency anchor = the return `VersionHistory` row.
2. **New** `NotificationEvent.SentToAuditAuditor (21)` (UPDATED 2026-06-18, FR-018) — recipients: a **new Auditor bucket** = users holding the `Auditor` role whose `UserGroupMembership` overlaps the applicant's stage groups (same group-scoped resolution reviewers use); applicant + actor excluded. Enqueued on every `SendToAudit`/`ResendToAudit` (entry to `PendingAudit`). CTA `"/Audit/{id}"`. This makes auditors "receive notifications the same way reviewers do." Requires adding an **Auditor `RecipientBucket`** + a resolver query mirroring the reviewer group-overlap join but filtered to the `Auditor` role.
3. **Re-point** the existing `AgreementGeneratedApplicant (14)` "ready to sign" notification: **remove** the enqueue from `FundingAgreementService.PersistGenerationAsync` (`:113`) and **add** it at the **release** action (`ReleaseForSignature`), anchored on the release `VersionHistory` row. Same template, no new enum value.

**Rationale:** Matches the spec (new return email §25.4; auditor notification §18/FR-018; re-point §25.2). The outbox 4-tuple idempotency key `(EventType, ApplicationId, VersionHistoryId, RecipientUserId)` (`dbo.NotificationDelivery.sql:30`) makes a fresh `VersionHistoryId` per release/return/send safe. Email-send failure does not block the transition (existing outbox resilience → FR-011 edge case). The Auditor bucket reuses the existing group-overlap recipient query, swapping the role filter from `REVIEWER` to `AUDITOR`.

---

## D11 — PDF generation + "PDF is correct" confirmation

**Decision:** PDF generation stays in `FundingAgreementController` but is **re-gated** to the auditor stage: `CanUserGenerateFundingAgreement` becomes "Auditor or Admin" and `CanGenerate/Regenerate` requires `State == PendingAudit` + audit-checklist-compliant. Add to `FundingAgreement`: `AuditorConfirmedAtUtc : DateTime?`, `AuditorConfirmedByUserId : string?`, and `ConfirmByAuditor(userId)`; `Replace()` (regenerate) **clears** the confirmation (edge case: regenerate invalidates confirm). Release is blocked until `AuditorConfirmedAtUtc` is set (D3).

**Rationale:** Reuses the entire existing PDF render/upload/persist path (`FundingAgreementController.Generate`, `FundingAgreementService.PersistGenerationAsync`) — only the authorization and state gate change, plus the confirm flag. The reviewer's direct generate action is removed (FR-005/FR-013); admins retain it via the Auditor-or-Admin gate.

---

## D12 — Role refusal patterns (FR-016)

**Decision:** Auditor surfaces gate role via `[Authorize(Roles = "Auditor,Admin")]` → **403** for wrong role (mirrors slice-A `SupplierAdminOnlyAttribute`). **Group overlap** is then enforced exactly as on the reviewer detail page: a non-admin auditor whose groups don't overlap the applicant's → **`Forbid()` (403)** (mirrors `ReviewController.Review` + `ApplicantSharesAnyGroupAsync`). Wrong-state on a mutation endpoint → **es-CR domain-state refusal** (existing `InvalidOperationException` → user-facing translation). Application-not-found → 404.

**Rationale:** Updated for group-scoping (D7). The auditor stage now matches the reviewer authorization shape (role gate + group-overlap Forbid), not the global no-disclosure 404 of `FundsUsageEvidenceController`. 403 = "you lack the role or the group"; the inbox simply omits out-of-group applications (empty-result, not an error).

---

## D13 — dacpac: new tables + columns + seed

**Decision:**
- Three new tables (`dbo.ChecklistTemplates`, `dbo.ChecklistTemplateItems`, `dbo.ApplicationChecklistResponses`) authored as `Tables/*.sql`, mirroring `dbo.FundsUsageEvidence.sql` (IDENTITY PK, FK `ON DELETE NO ACTION`, `RowVersion ROWVERSION`, CK where useful, NC index on FKs). Auto-included by the sqlproj `Tables/*.sql` glob.
- Two columns added to `dbo.FundingAgreements.sql`: `AuditorConfirmedAtUtc DATETIME2 NULL`, `AuditorConfirmedByUserId NVARCHAR(450) NULL` — nullable, migration-safe on populated DBs.
- New post-deploy `PostDeployment/07_SeedChecklistTemplates.sql`: idempotent (`IF NOT EXISTS` by template name) default `Both` template + a handful of es-CR items + `SCOPE_IDENTITY()` for children (the spec-035 ImpactTemplate seed pattern, `SeedData.sql:217`). Register via `<Build Remove>` + `<None Include>` and an `:r .\07_SeedChecklistTemplates.sql` line in `SeedData.sql` (after `06_`).

**Rationale:** Greenfield, no backfill. `ApplicationChecklistResponse.ChecklistTemplateItemId` FK is NO ACTION (templates/items are never hard-deleted, only deactivated) so historical responses survive. The two `FundingAgreements` columns are additive nullable (no data loss; safe with the `--no-drop` Azure publish posture in CLAUDE.md).

---

## D14 — E2E rewiring (cross-cutting ripple)

**Decision:** The reviewer/admin "Generate agreement" path is replaced by "send to audit → auditor generates". Affected and rewired:
- `FundingAgreementSeeder` gains `SeedPendingAuditApplicationAsync(appId, …)` (sets `State = 7`, attaches reviewer-checklist-complete state) and keeps `SeedExecutedAgreementAsync` for downstream-signing tests. `SeedGeneratedAgreementAsync` is repositioned to seed a released (post-audit) agreement at `ResponseFinalized`.
- `FundingAgreementTests` (US1 admin-generates, US3 reviewer-regenerates) → re-pointed to the auditor actor; `GenerateAgreementQueueTests` (reviewer "ready to generate" tab) → becomes/feeds the auditor inbox; `SigningWayfindingTests` seeding routes through audit before signing.
- New E2E: `AuditorWorkflowTests` (US1), `ReviewerSendToAuditTests` (US2), `AuditReturnTests` (US3), `ChecklistTemplateAdminTests` (US4). Auditor signs in via the seeded `auditor@programa-semilla.test` / `Demo123!` or the `/Account/SeedUser` + `/Account/AssignRole` dev seam. **Group-scope (UPDATED):** the test auditor must be assigned to the applicant's group(s) — reuse the existing `/Account/AssignAllGroups` seam (the seeded `auditor@` and `reviewer@` share the seeded groups), and add an out-of-group auditor negative test (empty inbox + 403 on the detail page), mirroring the reviewer-scope E2E.

**Rationale:** This is the known cross-cutting cost (flagged in the spec risk table). Per CLAUDE.md delivery bar, the gate is filtered E2E for the affected classes, not the full suite.

---

## Open items deferred to tasks/implementation (non-blocking)

- Seeded default template stage pinned to `Both` (FR-002 made unambiguous) — **confirmed here as `Both`**.
- One "Approve for agreement" button vs. folding approval into "checklist all-compliant → generate enabled" — UI detail; default: an explicit Approve action that records a `VersionHistory` "audit approved" entry and unlocks generation.
- Whether to extract a no-auto-transition read projection for the auditor view (D8) — decided at task time; default is reuse with the safe-branch note.
- Generating-actor recorded on the agreement is now the auditor/admin (`GeneratedByUserId`) — no schema change; the existing column captures it.
