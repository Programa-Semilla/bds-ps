# Review Brief: Auditor Role + Provider Regulatory Compliance Model

**Spec:** specs/038-auditor-provider-compliance/spec.md
**Generated:** 2026-06-17

> Reviewer's guide to scope and key decisions. See full spec for details.

---

## Feature Overview

Foundation slice (A) of feedback round 3. It elevates the existing supplier-administration role into an **Auditor** authority over provider compliance, replaces the four true/false provider compliance checkboxes with **enumerated Spanish-language statuses** (Hacienda 8 / CCSS 8 / SICOP 5) plus a PME/PYME flag, removes the unwanted electronic-invoice control entirely, and makes every regulatory change **auditable** with per-field "last reviewed" freshness shown during review. It also adds an informational provider **warning** and an email to auditors when a provider is created. It deliberately *tracks and displays* freshness without enforcing it — enforcement, the recommendation algorithm, the auditor workflow stage, and Hacienda API automation are sibling slices B/C/D.

## Scope Boundaries

- **In scope:** Auditor role (rename of SupplierAdmin); enumerated compliance statuses; e-invoice removal; PME/PYME flag; regulatory audit trail; per-field last-reviewed metadata + display; "reviewed — no change" action; provider warning flag/note; new-provider email to auditors.
- **Out of scope:** recommendation algorithm + delivery/warranty quote fields (B); auditor application-workflow stage, checklists, inbox, PDF moves (C); 1-month staleness blocking + daily Hacienda API sync (D); any in-app notification center.
- **Why these boundaries:** A is the keystone the other slices depend on (B needs the enums; C/D need the role + audit/timestamp fields). Keeping A to "model + track + display" makes it self-contained and shippable alone.

## Critical Decisions

### Rename SupplierAdmin → Auditor (absorb, don't coexist)
- **Choice:** the existing `SupplierAdmin` role *becomes* `Auditor`; members migrate; SupplierAdmin stops being seeded.
- **Trade-off:** a behavioral rename of an existing role vs. two near-duplicate roles.
- **Feedback:** is any current SupplierAdmin data/integration relying on the literal role name?

### Greenfield, no backfill for compliance
- **Choice:** drop the boolean flags, add nullable status columns, do not translate old true/false into statuses.
- **Trade-off:** old compliance signal is discarded; every existing provider reads "unreviewed" until an auditor sets values.
- **Feedback:** confirm no dev/prod supplier compliance data is worth preserving.

### New-provider notification = email-only, direct-send
- **Choice:** email all auditors via the spec-033 direct-send pattern, bypassing the application-scoped outbox; no in-app notification.
- **Trade-off:** reuses existing email infra cleanly; auditors who don't read email won't see an in-app cue.

## Areas of Potential Disagreement

### A/D boundary: tracking here, enforcing later
- **Decision:** "last reviewed" timestamps, the audit trail, and the "reviewed — no change" action live in A; the 1-month *block* and Hacienda API live in D.
- **Why this might be controversial:** the "reviewed — no change" action has limited visible payoff until D's blocking exists.
- **Alternative view:** bundle freshness tracking + enforcement into one slice.
- **Seeking input on:** comfort with shipping freshness *visibility* before *enforcement*.

### Audit-trail storage approach (deferred to plan)
- **Decision:** likely extend the generic `AdminAuditEvent` rather than a dedicated provider-audit table.
- **Why this might be controversial:** the desired audit fields (previous/new value, source, reviewedBy) are richer than a generic payload expresses ergonomically.
- **Seeking input on:** preference for reusing AdminAuditEvent vs. a purpose-built table.

## Naming Decisions

| Item | Name | Context |
|------|------|---------|
| Role | `Auditor` | successor to `SupplierAdmin` |
| Compliance label | `SICOP` (canonical) | `CCOP` alias dropped (§28.4) |
| Status values | exact Spanish strings (§13) | preserved verbatim per §28.5 |
| Provider flags | `IsPmeOrPyme`, `HasWarning` + `WarningNote` | provider-level |

## Open Questions

- [ ] es-CR UI display label for the Auditor role ("Auditor" vs "Auditoría").
- [ ] Is "reviewed — no change" available before any value is set, or disabled until a status exists?
- [ ] Maximum length for the warning note.

## Risk Areas

| Risk | Impact | Mitigation |
|------|--------|------------|
| Role rename ripples through auth checks, seeds, E2E fixtures | Med | inventory all `SupplierAdmin` references at plan; keep capability parity (FR-002) |
| Removing e-invoice misses a hidden reference (filter/validation/report) | Med | FR-008 mandates removal from *every* surface; grep sweep at implementation |
| Provider-scoped notification doesn't fit the app-scoped outbox | Low | direct-send (spec-033 pattern), best-effort, logged (FR-024) |
| Verbatim Spanish enum values mistyped | Med | values fixed in FR-005/006/007; verify against source at implementation |

---
*Share with reviewers before implementation.*
