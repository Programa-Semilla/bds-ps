# Feature Specification: Auditor Role + Provider Regulatory Compliance Model

**Feature Branch**: `038-auditor-provider-compliance`
**Created**: 2026-06-17
**Status**: Draft
**Input**: feedback-3 slice A (foundation). Source: `seeds/feedback-3/AI_Coding_Agent_Unified_Requirements.md` §2.3, §9, §10, §13, §15.1–15.4/15.6/15.7, §22.5/22.6/22.11A, §23.1, §25.1, §28.4/28.5. Decomposition map: `seeds/feedback-3/00-decomposition.md` (row A).

## Purpose

Establish the foundation for provider (supplier) regulatory governance. Introduce an **Auditor** authority over provider compliance, replace the current true/false compliance checkboxes with explicit **enumerated statuses**, and make every regulatory change **auditable** and **freshness-aware**. This is the keystone slice of feedback round 3: later slices build on it — the multi-criteria recommendation algorithm (B) reads these statuses, the auditor application-workflow stage (C) turns the role into a workflow actor, and freshness enforcement + Hacienda API automation (D) consume the audit trail and last-reviewed metadata introduced here.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Auditor manages provider regulatory compliance (Priority: P1)

The existing supplier-administration role is elevated to an **Auditor** who owns provider regulatory compliance. The auditor opens a provider, sets each regulatory status from a defined list of values (rather than ticking on/off boxes), marks whether the provider is a PME/PYME, and the obsolete "electronic invoice" control is gone. All current supplier-administration capabilities (list, detail, edit, verify, reject, branch edit) continue to work for this role.

**Why this priority**: This is the MVP. It delivers the core conceptual change the client demanded — enumerated compliance statuses owned by a clear authority — and removes the unwanted electronic-invoice control. Everything else (audit trail, warnings, notifications) decorates this surface. Without it, no other slice can proceed.

**Independent Test**: Sign in as the auditor, open a provider, confirm there is no electronic-invoice control, set Hacienda/CCSS/SICOP each to one of their listed values plus the PME/PYME flag, save, reopen, and confirm the chosen values persist and display.

**Acceptance Scenarios**:

1. **Given** a user who held the former supplier-administration role, **When** they sign in after this change, **Then** they have the Auditor role and can reach every provider-administration screen they could before.
2. **Given** the provider edit screen, **When** the auditor views it, **Then** the electronic-invoice control is absent everywhere it previously appeared (edit, detail, list filters, validation).
3. **Given** a provider with no compliance values set, **When** the auditor opens it, **Then** each regulatory status shows an "unreviewed" indication and no error occurs.
4. **Given** the provider edit screen, **When** the auditor selects a Hacienda status, a CCSS status, a SICOP status, and toggles PME/PYME, **Then** the choices are constrained to the defined value lists and persist on save.
5. **Given** an existing provider whose old boolean compliance flags were set, **When** the auditor opens it after the change, **Then** the new statuses read as "unreviewed" (old true/false values are not translated into statuses).

---

### User Story 2 - Regulatory changes are auditable and freshness is visible (Priority: P2)

Every change an auditor makes to a regulatory value is recorded with what changed, who changed it, and when. Each regulatory status carries "last reviewed" information so reviewers and auditors can see how current it is ("last reviewed 15 days ago by …"). When a status is still valid after time has passed, the auditor can record a review **without changing the value**, refreshing its freshness.

**Why this priority**: Auditability and freshness visibility are explicit client requirements and are the data foundation that slice D's staleness enforcement and Hacienda automation will consume. They add value on their own (traceability + an at-a-glance "how fresh is this") even before any blocking exists.

**Independent Test**: As the auditor, change a regulatory status and confirm an audit entry captures the old value, new value, actor, and time; then use the "reviewed — no change" action and confirm a new audit entry is recorded and the "last reviewed" timestamp advances while the value stays the same.

**Acceptance Scenarios**:

1. **Given** a provider, **When** the auditor changes any regulatory status, **Then** an audit entry records the field, previous value, new value, the auditor, the timestamp, and a source tag of "manual".
2. **Given** a provider, **When** the auditor changes the PME/PYME flag or the warning flag/note, **Then** an audit entry is recorded for that change too.
3. **Given** a provider with a set regulatory status, **When** the auditor records "reviewed — no change", **Then** the value is unchanged, the "last reviewed" timestamp and reviewer for that field advance, and an audit entry marks it as reviewed-unchanged.
4. **Given** a reviewer or auditor viewing the provider information during application review, **When** the screen renders, **Then** each regulatory value displays when it was last reviewed and by whom.

---

### User Story 3 - Provider warnings highlight providers during review (Priority: P2)

An auditor can flag a provider with a warning and a free-text note explaining why. The warning is shown prominently to reviewers and auditors while they review an application that uses that provider. The warning calls attention but never blocks the application on its own.

**Why this priority**: The client wants to highlight (not necessarily reject) providers, with a reason. It is independent of the compliance-status work and can ship separately, but it shares the same provider surface so it groups naturally with this slice.

**Independent Test**: As the auditor, set a warning flag + note on a provider; as a reviewer, open an application that uses that provider and confirm the warning and its note are visible; confirm the reviewer cannot author/edit the warning and that the application is not blocked.

**Acceptance Scenarios**:

1. **Given** the provider screen, **When** the auditor sets the warning flag and enters a note, **Then** both persist and an audit entry is recorded.
2. **Given** a provider with a warning, **When** a reviewer or auditor reviews an application using that provider, **Then** the warning and its note are shown prominently.
3. **Given** a provider with a warning, **When** a reviewer views it, **Then** the reviewer can see the warning but cannot create or edit it.
4. **Given** a provider with a warning, **When** an application that uses it is processed, **Then** the warning alone does not prevent the application from advancing.

---

### User Story 4 - Auditors are notified when a provider is created (Priority: P3)

Whenever a provider is created — by any path — all auditors receive an email prompting them to review the provider's regulatory compliance, with enough information to identify and open the provider.

**Why this priority**: It reduces operational friction (auditors learn that a provider needs review) but is not required for the compliance model itself to function, so it ships last in this slice.

**Independent Test**: Create a provider and confirm every auditor receives an email containing the provider name, identification number, creation time, creator, and a link to the provider's detail screen (captured in the test mail sink during E2E).

**Acceptance Scenarios**:

1. **Given** any provider-creation path, **When** a provider is created, **Then** an email is sent to all users holding the Auditor role.
2. **Given** the notification email, **When** an auditor reads it, **Then** it contains the provider name, identification number (if available), creation date/time, the creating user (if available), a link to the provider detail/review screen, and a prompt to review compliance.
3. **Given** a non-production environment with a recipient allowlist, **When** the notification is sent, **Then** only allowlisted recipients receive it and non-allowlisted recipients are dropped (the auditor seed account is allowlisted).
4. **Given** the email service fails, **When** a provider is created, **Then** the failure is logged and provider creation still succeeds.

---

### Edge Cases

- **Provider with no statuses set**: all three regulatory values render as "unreviewed" ("sin revisar"); nothing errors; the recommendation algorithm (slice B) will later treat unset as the non-winning baseline.
- **"Reviewed — no change" on an unset status**: either records a review timestamp without inventing a value, or the action is unavailable until a value exists — to be settled at plan (see Open Questions).
- **Concurrent edits to the same provider**: resolved by the existing optimistic-concurrency mechanism; the loser is told to reload, consistent with current behavior.
- **Notification with zero auditors**: nothing is sent; provider creation still succeeds (logged).
- **Long/empty warning note**: an empty note with the flag off clears the warning; the note has a sensible maximum length (settle at plan).
- **Former supplier-admin demo/seed account**: replaced by an auditor-equivalent seed that is covered by the non-production recipient allowlist so notifications are testable.

## Requirements *(mandatory)*

### Functional Requirements

**Role (US1)**

- **FR-001**: The system MUST replace the existing supplier-administration role with an **Auditor** role: existing members of the former role become Auditors, the former role is no longer seeded, and the seeded demo account for that role is replaced by an auditor-equivalent account that is covered by the non-production recipient allowlist.
- **FR-002**: The Auditor role MUST retain every capability the former supplier-administration role had (provider list, detail, edit, verify, reject, branch edit), plus the new compliance capabilities below.
- **FR-003**: The existing provider verification lifecycle (Draft → PendingReview → Verified | Rejected, with verifier/verified-at/rejection-reason) MUST be retained unchanged and owned by the Auditor role.

**Compliance status model (US1)**

- **FR-004**: The system MUST represent provider Hacienda, CCSS/Caja, and SICOP compliance as **enumerated status** values selected from a fixed list (replacing the previous on/off checkboxes), where an unset status is permitted and means "unreviewed".
- **FR-005**: The Hacienda status list MUST be exactly these values (preserved verbatim, Spanish): `sin inscripción`, `al día`, `estado moroso`, `cobro administrativo`, `desinscrito al día`, `sin información`, `desinscrito moroso`, `desinscrito de oficio`.
- **FR-006**: The CCSS/Caja status list MUST be exactly these values (preserved verbatim, Spanish): `sin inscripción`, `al día`, `estado moroso`, `cobro administrativo`, `estado inactivo / al día`, `estado inactivo / moroso`, `sin información`, `cobro judicial`.
- **FR-007**: The SICOP status list MUST be exactly these values (preserved verbatim, Spanish): `inhabilitación`, `sin sanciones`, `sin suscripción`, `con sanciones`, `suspensión`. `SICOP` is the canonical label everywhere; the `CCOP` alias is dropped.
- **FR-008**: The system MUST remove the provider "electronic invoice" control completely — from the provider data model, the create/edit/detail screens, any list filter or validation, and any other surface implying it is a current requirement.
- **FR-009**: The system MUST add a provider-level PME/PYME flag that the auditor can set and that is displayed on the provider screen. (Its scoring effect is out of scope here — slice B.)
- **FR-010**: The system MUST NOT translate previous boolean compliance values into statuses (greenfield, no backfill): existing providers read as "unreviewed" until an auditor sets values.
- **FR-011**: Regulatory status selection MUST be constrained to the defined value lists; arbitrary/free-text status values MUST be rejected.

**Audit trail + freshness (US2)**

- **FR-012**: The system MUST record an audit entry for every change to a provider regulatory status, capturing the field, previous value, new value, the acting user, the timestamp, and a source tag (`manual` for human changes; `api`/`system` are reserved for slice D).
- **FR-013**: Audit-trail coverage MUST also include changes to the PME/PYME flag and to the warning flag/note.
- **FR-014**: The system MUST maintain, per regulatory value (Hacienda, CCSS, SICOP), "last reviewed" metadata: when it was last reviewed, by whom, and the source of that review.
- **FR-015**: The system MUST provide a "reviewed — no change / re-authorize" action that updates the last-reviewed metadata and records an audit entry **without** changing the stored status value.
- **FR-016**: The system MUST display, on the provider screen and within the provider information shown during application review, when each regulatory value was last reviewed and by whom.

**Warnings (US3)**

- **FR-017**: The system MUST support a provider warning flag plus a free-text warning note.
- **FR-018**: Auditors MUST be able to create and edit provider warnings; reviewers MUST be able to see warnings but MUST NOT be able to create or edit them.
- **FR-019**: The system MUST show a provider's warning and its note prominently to reviewers and auditors while reviewing an application that uses that provider.
- **FR-020**: A warning MUST be informational only and MUST NOT, by itself, block an application from advancing.

**Notification (US4)**

- **FR-021**: When a provider is created by any path, the system MUST send an email to all users holding the Auditor role prompting them to review the provider's regulatory compliance.
- **FR-022**: The notification email MUST include the provider name, identification number (if available), creation date/time, the creating user (if available), a link to the provider detail/review screen, and a review prompt.
- **FR-023**: The notification MUST honor the existing non-production recipient allowlist; in non-production, only allowlisted recipients receive it.
- **FR-024**: A notification-send failure MUST be logged and MUST NOT prevent provider creation from succeeding.

**Cross-cutting**

- **FR-025**: All new user-facing copy MUST be in es-CR, consistent with the platform default culture; the preserved Spanish status values are shown as-is.

### Key Entities *(include if feature involves data)*

- **Provider (Supplier)**: the company quoted on item lines. Gains enumerated `HaciendaStatus`, `CcssStatus`, `SicopStatus` (each nullable = unreviewed); per-field last-reviewed metadata (when / by whom / source) for each of the three; a `IsPmeOrPyme` flag; a warning flag + free-text warning note. Loses the electronic-invoice flag. Retains identity, verification lifecycle, and branches unchanged.
- **Regulatory audit entry**: an append-only record of a regulatory-relevant change or review — field, previous value, new value, event kind (changed vs reviewed-no-change), source (manual now; api/system reserved), acting user, timestamp, optional note. Extends the platform's existing administrative audit-event mechanism.
- **Auditor role**: the authority over provider compliance; successor to the former supplier-administration role; gains the new compliance capabilities. (Its application-workflow responsibilities are introduced in slice C, not here.)
- **New-provider notification**: a provider-scoped email to all auditors triggered on provider creation; sent directly (not via the application-scoped notification outbox).

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: An auditor can set every regulatory status, the PME/PYME flag, and a warning on a provider, and those values persist and display on reload — with the electronic-invoice control absent from every provider surface.
- **SC-002**: 100% of regulatory-value changes and "reviewed — no change" actions produce an audit entry capturing old/new value, actor, time, and source.
- **SC-003**: During application review, each provider regulatory value shows its last-reviewed recency and reviewer, and any provider warning is visible to reviewers and auditors.
- **SC-004**: Creating a provider results in an email to all auditors (observed in the test mail sink during E2E), and a simulated send failure does not prevent the provider from being created.
- **SC-005**: A former supplier-administrator can perform every provider-administration action they could before, now as an Auditor, with no loss of capability.
- **SC-006**: Only the defined Spanish status values are selectable for each regulatory field; no other value can be stored.

## Assumptions

- The existing provider/supplier aggregate, the supplier-administration screens, the administrative audit-event mechanism, the email-sending seam with its non-production allowlist, and Identity role seeding are reused rather than rebuilt.
- "Greenfield, no backfill" is acceptable for compliance data: there is no business need to preserve the meaning of the old boolean flags.
- The new-provider notification is email-only; there is no in-app notification center to integrate with, and building one is out of scope.
- The provider verification lifecycle and branch management are unchanged by this slice.
- Reviewers see provider warnings and freshness during the review surfaces that already exist; this slice does not add new review workflow states (that is slice C).

## Out of Scope

- The multi-criteria supplier **recommendation algorithm** and the quote-level **delivery-lead-time / warranty** fields it needs (slice B).
- The **auditor application-workflow stage**: new audit state, reviewer/auditor checklist templates, auditor inbox, and moving PDF generation + correctness confirmation to the auditor (slice C).
- **Enforcement** of regulatory freshness — the one-month staleness **block** on application progress — and the **daily Hacienda API synchronization** job (slice D). This slice only *tracks and displays* freshness and lets auditors record reviews.
- Any **in-app notification** surface.

## Open Questions

- The es-CR UI display label for the Auditor role (e.g., "Auditor" vs "Auditoría").
- Whether "reviewed — no change" is available before any value is set, or disabled until a status exists.
- Maximum length for the warning note.
