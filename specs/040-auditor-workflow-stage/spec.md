# Feature Specification: Auditor Workflow Stage

**Feature Branch**: `040-auditor-workflow-stage`
**Created**: 2026-06-18
**Status**: Draft
**Input**: feedback-3 slice C. Source: `seeds/feedback-3/AI_Coding_Agent_Unified_Requirements.md` §11, §12, §18, §19, §22.9/22.10/22.11, §23.1/23.2, §25.2/25.4, §28.9. Decomposition map: `seeds/feedback-3/00-decomposition.md` (row C). Depends on shipped slice A (`specs/038-auditor-provider-compliance`). Resolves open decision §28.9.

## Purpose

Insert a mandatory **Auditor workflow stage** between reviewer completion and the funding agreement reaching the applicant for signature. Today, once an application reaches `ResponseFinalized`, a reviewer or administrator generates the funding-agreement PDF and it goes straight into the signing ceremony. This slice interposes an audit: the reviewer completes a **reviewer checklist** and hands the application to an **auditor**; the auditor independently completes an **audit checklist**, and only on approval does the auditor **generate the PDF, confirm it is correct, and release it for signature**. If the auditor finds non-compliance, the application is **returned to the reviewer** (never directly to the applicant) with a reason per failed item.

This turns the Auditor role (created in slice A) into a workflow actor and moves agreement PDF generation from the reviewer to the auditor. The open §28.9 decision is resolved as **per-stage checklist templates** (`appliesToStage = reviewer | auditor | both`). The downstream signing ceremony (applicant signs → reviewer verifies → `AgreementExecuted`) is **unchanged**.

## Workflow Context

The application lifecycle today is:

```
Draft → Submitted → UnderReview →[reviewer Finalize]→ Resolved
   →[applicant per-item Accept/Reject response]→ ResponseFinalized →[appeal loop, if any]
   →[reviewer/admin generates agreement PDF]→ signing ceremony → AgreementExecuted
```

This slice changes only the segment between `ResponseFinalized` and the signing ceremony by inserting two new states:

```
… ResponseFinalized (no open appeal)
   →[reviewer completes reviewer checklist + "Send to audit"]→ PendingAudit
   →[auditor completes audit checklist]
        ├─ all compliant → Approve → auditor generates PDF → confirms PDF correct → Release
        │                                                        → signing ceremony → AgreementExecuted (unchanged)
        └─ any non-compliant → Return to reviewer → ReturnedFromAudit
                                                       →[reviewer reworks + re-completes checklist + re-sends]→ PendingAudit
```

The previous direct path (`ResponseFinalized` → generate agreement → signing) is **removed**. The reviewer's former "Generate agreement" action becomes **"Send to audit."**

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Auditor takes an application through audit to signature (Priority: P1)

An auditor opens their inbox, picks an application awaiting audit, reviews everything needed to judge it, works through the audit checklist, approves it, generates and reviews the agreement PDF, confirms the PDF is correct, and releases it to the applicant for digital signature.

**Why this priority**: This is the MVP and the heart of the slice — it is the new mandatory gate every application must pass before an agreement can be signed. Without it, the auditor role has no workflow authority and the client's core requirement (an audit before agreement generation) is unmet.

**Independent Test**: Sign in as the auditor, open an application in `PendingAudit`, confirm full read access to the application data and provider compliance information, mark every required audit item compliant, approve, generate the PDF, check the "PDF is correct" confirmation, release for signature, and confirm the application enters the signing ceremony and the applicant receives the "ready to sign" notification.

**Acceptance Scenarios**:

1. **Given** an application in `PendingAudit`, **When** the auditor opens it, **Then** they see application details, applicant information, requested items, provider information including regulatory statuses + freshness + warnings, impact/category data, supporting documents, existing PDFs, and review history, and can download documents and PDFs.
2. **Given** the audit checklist has required items, **When** any required item is not yet marked compliant, **Then** the "Approve for agreement" action is unavailable.
3. **Given** every required audit item is marked compliant, **When** the auditor approves, **Then** the auditor can generate the agreement PDF (same content as the agreement generated today).
4. **Given** a generated PDF that the auditor has not yet confirmed, **When** the auditor attempts to release it for signature, **Then** the action is unavailable until the "PDF is correct" confirmation is checked.
5. **Given** a generated and confirmed PDF, **When** the auditor releases it for signature, **Then** the application enters the existing signing ceremony, the applicant receives the existing "ready to sign" notification, and the downstream signing flow proceeds unchanged.

---

### User Story 2 - Reviewer completes the checklist and sends to audit (Priority: P1)

When a reviewer judges an application ready (it has reached `ResponseFinalized` with no open appeal), the reviewer works through the reviewer checklist beside the application data and sends it to audit. The reviewer can no longer generate the agreement directly.

**Why this priority**: The auditor stage cannot receive any work until the reviewer hand-off exists. It is the entry point of the new gate and is required for US1 to have any input. Equal P1.

**Independent Test**: Sign in as the reviewer, open an application in `ResponseFinalized`, confirm the old "Generate agreement" action is gone, check each required reviewer checklist item, confirm "Send to audit" is disabled until all required items are checked, send to audit, and confirm the application moves to `PendingAudit` and appears in the auditor inbox.

**Acceptance Scenarios**:

1. **Given** an application in `ResponseFinalized` with no open appeal, **When** the reviewer views it, **Then** the reviewer checklist is shown alongside the application data and the direct "Generate agreement" action is absent.
2. **Given** the reviewer checklist has required items, **When** any required item is unchecked, **Then** the "Send to audit" action is disabled.
3. **Given** all required reviewer items are checked, **When** the reviewer sends to audit, **Then** the application transitions to `PendingAudit` and each check is recorded with who checked it and when.
4. **Given** an application not in `ResponseFinalized` (or with an open appeal), **When** a reviewer attempts to send to audit, **Then** the action is refused.

---

### User Story 3 - Auditor returns a non-compliant application to the reviewer (Priority: P2)

When the auditor finds one or more checklist items non-compliant, they record a reason for each and return the application to the reviewer. The reviewer is notified, sees the reasons, makes corrections, and re-sends to audit. The applicant is never contacted by this path.

**Why this priority**: The return path is essential for a real audit (audits that can only approve are not audits), but the forward/approval path (US1+US2) is a usable MVP on its own, so this is P2.

**Independent Test**: As the auditor, mark at least one audit item non-compliant with a reason and confirm "Approve" is unavailable while "Return to reviewer" is available; return the application; confirm it moves to `ReturnedFromAudit`, the assigned reviewer receives the return email with the non-compliant items and reasons, and the applicant receives nothing. As the reviewer, open the returned application, see the reasons, re-complete the reviewer checklist, and re-send to audit.

**Acceptance Scenarios**:

1. **Given** an application in `PendingAudit`, **When** the auditor marks an item non-compliant, **Then** a reason is required for that item and "Approve for agreement" becomes unavailable.
2. **Given** at least one non-compliant item with a reason, **When** the auditor returns the application, **Then** it moves to `ReturnedFromAudit`, the per-item reasons are persisted, and a return-to-reviewer email is sent to the assigned reviewer/group containing the application identifier, applicant name, auditor name, the non-compliant items with reasons, and a deep link.
3. **Given** an application in `ReturnedFromAudit`, **When** the assigned reviewer opens it, **Then** they see the auditor's non-compliance reasons and the applicant has not been contacted.
4. **Given** an application in `ReturnedFromAudit`, **When** the reviewer re-completes the required reviewer checklist and re-sends, **Then** it returns to `PendingAudit`.
5. **Given** the return email send fails, **When** the auditor returns the application, **Then** the state change to `ReturnedFromAudit` still succeeds and the failure is logged.

---

### User Story 4 - Administrator manages reviewer and auditor checklist templates (Priority: P2)

An administrator configures the checklist templates that drive both gates: a simple ordered list of text items, each markable required or optional, with the template declaring whether it applies to the reviewer stage, the auditor stage, or both.

**Why this priority**: The gates are usable with the seeded default template, so administration is valuable but not blocking for an MVP — hence P2. It gives the client control over what reviewers and auditors must verify.

**Independent Test**: As an administrator, create a template, set its stage applicability, add and reorder text items, mark some required, activate it, and confirm the reviewer and auditor gates use the applicable items. Then edit an item's text and confirm previously recorded responses on existing applications are unchanged.

**Acceptance Scenarios**:

1. **Given** the checklist administration screen, **When** the administrator creates a template, **Then** they can set its name, optional description, stage applicability (`reviewer`, `auditor`, or `both`), active flag, and an ordered list of text-only items each with a required flag and active flag.
2. **Given** an active template applicable to a stage, **When** a reviewer or auditor reaches that gate, **Then** the gate presents that template's active items in display order.
3. **Given** a template marked `both`, **When** either gate is reached, **Then** that template satisfies the gate for both stages.
4. **Given** an application with recorded checklist responses, **When** the administrator edits the template's items, **Then** the already-recorded responses are preserved exactly as captured.
5. **Given** no active template applies to a stage, **When** that gate is reached, **Then** the gate has zero required items and its advance action is immediately available (degenerate pass).

---

### Edge Cases

- **Empty / all-inactive checklist for a stage**: with no applicable active template, a gate has zero required items and its advance action is immediately enabled. The seeded default template prevents this in practice; this behavior is an explicit, accepted decision rather than an error.
- **Concurrent auditors on the same application**: two auditors act on the same `PendingAudit` application; concurrency is handled consistently with existing aggregates (optimistic concurrency) — one action wins and the other receives a stale-state refusal.
- **Template edited mid-audit**: responses already captured are preserved; newly added required items are not retroactively forced onto an already-submitted checklist response.
- **Re-send loop**: an application may cycle `PendingAudit ⇄ ReturnedFromAudit` any number of times.
- **Regenerate after confirm**: if the auditor regenerates the PDF after confirming it, the prior "PDF is correct" confirmation is invalidated and must be re-confirmed before release.
- **Appeal interplay**: appeals resolve before audit (audit is strictly post-`ResponseFinalized` with no open appeal); this slice introduces no new audit↔appeal interaction.

## Requirements *(mandatory)*

### Functional Requirements

#### Checklist template administration
- **FR-001**: Administrators MUST be able to manage checklist templates, where each template has a name, an optional description, a stage applicability of `reviewer`, `auditor`, or `both`, an active flag, and an ordered list of text-only items; each item has its text, a display order, a required flag, and an active flag.
- **FR-002**: The system MUST apply one active template per stage (global, not scoped to Fund or Process); a template marked `both` satisfies both stages. The system MUST seed a default template so the workflow is usable out of the box.
- **FR-003**: Editing a template or its items MUST NOT alter checklist responses already recorded on applications; historical responses MUST be preserved exactly as captured.

#### Reviewer gate
- **FR-004**: At `ResponseFinalized` with no open appeal, the reviewer MUST see the application alongside the applicable reviewer checklist and be able to check each required item; each check MUST capture who checked it and when.
- **FR-005**: The "Send to audit" action MUST be enabled only when all required reviewer items are checked, and it MUST transition the application to `PendingAudit`. The reviewer's former direct agreement-generation action MUST be removed.

#### Auditor inbox & review access
- **FR-006**: Auditors MUST have a global inbox listing every application in `PendingAudit` (applications in `ReturnedFromAudit` MUST NOT appear in the auditor inbox — they sit with the reviewer). The inbox MUST show enough to triage: applicant, identifiers, time the application entered audit, and provider(s) with regulatory/warning indicators.
- **FR-007**: An auditor MUST be able to open an application in audit with read access equivalent to a reviewer's: application details, applicant information, requested items, provider information including regulatory statuses + review freshness + warnings, impact/category data, supporting documents, existing or generated PDFs, and the application's review history; and MUST be able to download documents and PDFs.

#### Audit decision
- **FR-008**: The auditor MUST complete the applicable audit checklist by marking each item compliant or non-compliant; a non-compliant item MUST require a reason. Each mark MUST capture who made it and when.
- **FR-009**: The "Approve for agreement" action MUST be enabled only when all required audit items are marked compliant; on approval the auditor MUST be able to generate the agreement PDF, whose content is identical to the agreement generated today.
- **FR-010**: After generating the PDF, the auditor MUST review it and check an additional "PDF is correct" confirmation; only after that confirmation MUST the auditor be able to release the agreement to the applicant for signature. Releasing MUST start the existing signing ceremony and MUST trigger the existing "ready to sign" applicant notification at this new point.
- **FR-011**: If any audit item is marked non-compliant, the auditor's only forward action MUST be "Return to reviewer": the application MUST move to `ReturnedFromAudit`, the per-item reasons MUST be persisted, and a return-to-reviewer email MUST be sent to the assigned reviewer/group containing the application identifier, applicant name, auditor name, the non-compliant items with their reasons, and a deep link to the review screen.

#### Reviewer rework after return
- **FR-012**: On `ReturnedFromAudit`, the reviewer MUST see the auditor's non-compliance reasons, be able to make corrections, re-complete the required reviewer checklist, and re-send to audit (returning the application to `PendingAudit`). Auditor feedback MUST NOT be automatically exposed to the applicant. Re-engaging the applicant for more information, if needed, uses the system's existing reopen/appeal mechanisms and is out of scope here.

#### Permissions
- **FR-013**: Auditor-stage actions (open audit, complete the audit checklist, approve, generate PDF, confirm, release, return) MUST be available to a user holding the Auditor role OR an administrator. The reviewer MUST NOT be able to generate the agreement directly. Administrators retain their existing broad capabilities.

#### Auditability
- **FR-014**: Checklist completion (both stages), the send-to-audit, approve, generate, confirm/release, and return transitions MUST be recorded with the acting user and timestamp, consistent with the existing administrative/audit event logging.

#### Conventions
- **FR-015**: All user-facing copy introduced by this feature MUST be in es-CR, consistent with the rest of the platform.
- **FR-016**: Refusals MUST follow existing patterns: acting on an application not in the expected state is refused with an es-CR message consistent with existing domain-state guards; a non-auditor/non-admin attempting auditor actions is refused with a role-refusal (403) or no-disclosure (404) response mirroring existing reviewer-scope behavior.

### Key Entities *(include if feature involves data)*

- **Checklist Template**: A named, optionally described configuration that applies to the reviewer stage, the auditor stage, or both; carries an active flag and owns an ordered set of items. Suggested by §22.9.
- **Checklist Template Item**: A single text-only verification line belonging to a template, with a display order, a required flag, and an active flag. Suggested by §22.10.
- **Application Checklist Response**: A per-application record of a checklist outcome, keyed by stage (`reviewer` or `auditor`) and the template item, carrying a status (e.g., `checked` / `not_compliant`), an optional non-compliance reason, and the completing user and timestamp. Suggested by §22.11.
- **Application (state)**: Gains two new workflow states, `PendingAudit` and `ReturnedFromAudit`, between `ResponseFinalized` and the signing ceremony. Display names may be finalized during implementation; the workflow states themselves are required (§11.4).

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: No application reaches the signing ceremony without first passing through `PendingAudit` and an auditor's approval plus PDF-correctness confirmation.
- **SC-002**: "Send to audit" is impossible until every required reviewer item is checked, and "Approve for agreement" is impossible until every required audit item is marked compliant.
- **SC-003**: An auditor can take an application end-to-end (inbox → review → checklist → approve → generate PDF → confirm → release), after which the applicant receives the existing "ready to sign" notification.
- **SC-004**: A non-compliant audit returns the application to the reviewer with reasons captured and the reviewer notified, and the applicant is not contacted by this path.
- **SC-005**: Every checklist response and every new stage transition is attributable to a specific user and time.
- **SC-006**: The downstream signing ceremony and the resulting `AgreementExecuted` behavior are unchanged from today.

## Assumptions

- The slice A foundation (`038-auditor-provider-compliance`) is shipped: the Auditor role exists, and provider regulatory statuses, review freshness, and warnings are available to display on the auditor's review surface.
- The existing application state machine, funding-agreement PDF generation, signing ceremony, notification outbox (specs 021/028), and administrative audit-event logging are reused rather than replaced.
- The "ready to sign" applicant notification already exists; this slice re-points its trigger to the auditor's release action rather than introducing a new template.
- The agreement PDF content and template are unchanged; only the actor and the gating around generation change.
- Reviewer group-scoping (spec 016) continues to apply to reviewers; auditors are intentionally not group-scoped in this slice (global inbox).
- No new managed (NuGet) dependency is expected; existing outbox, PDF, audit-event, and storage seams are sufficient.

## Dependencies

- **Slice A — `038-auditor-provider-compliance` (shipped)**: Auditor role; provider regulatory statuses, warnings, and review-freshness data shown on the auditor review surface.
- **Existing platform**: application state machine; funding-agreement PDF generation and signing ceremony; notification outbox (specs 021/028); administrative audit-event logging; reviewer group model (reviewer side only).

## Out of Scope

- Per-process or per-fund checklist templates; group-scoped auditor inbox.
- A new "audit → applicant" route. Re-engaging the applicant after an audit finding uses the existing reopen/appeal machinery.
- Any change to the agreement PDF content/template, the signing ceremony, or `AgreementExecuted` behavior.
- Provider-compliance review-freshness **blocking** of application progress (slice D, §17). This slice only **displays** the slice-A freshness information to the auditor.
