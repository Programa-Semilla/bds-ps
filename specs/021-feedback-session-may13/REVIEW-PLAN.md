# Review Guide: Feedback Session May-13 (021)

**Spec:** [spec.md](spec.md) | **Plan:** [plan.md](plan.md) | **Tasks:** [tasks.md](tasks.md)
**Generated:** 2026-05-14

---

## What This Spec Does

Bundles 26 stakeholder refinements from the May-13 feedback session into a single delivery. The most consequential moves are structural: a new `Process` aggregate sits above `Group`, a `Plantilla` configuration template is taken as an immutable snapshot when assigned to a Process, `Impact` is lifted from `Item` to `Application`, and `Application` gains an opaque dictation-safe `PublicCode` (`A7K2-9XF`-shaped) that fully displaces the numeric *"Solicitud N.º N"* label. Around those moves, the spec attaches: a new `SupplierAdmin` role with strict scope, a CR `Province`/`Cantón` cascading catalog, a self-service `/profile` + forgot-password flow, stage-expiry windows with hourly reminder emails, a public `/` landing scaffold, an admin dashboard KPI repivot, an *acompañamiento* copy pivot, and a soft-delete dashboard-filter bug fix.

**In scope:** 34 FRs / 16 SCs / 8 user stories (US1-P1..US8-P3). Schema deltas via dacpac; new tables `Processes`, `Plantillas`, `ProcessPlantillas`, `Provinces`, `Cantons`, `PasswordResetTokens`; column adds on `Groups`, `Applications`, `Items` (drop `ImpactId`), `SupplierBranches`, `AspNetUsers`, `SystemConfigurations`. No new managed dependencies (NFR-005).

**Out of scope:** BCCR daily exchange-rate auto-fetch (research workstream); Tropic AI quotation extraction (PoC stays separate); OTP for sensitive profile edits; user-initiated email-change request workflow; foreign supplier addresses; multi-Process Applicant membership; admin-configurable reminder cadence; visual-regression tooling. These boundaries matter — see [spec.md Out of Scope](spec.md#out-of-scope) — because some of them (BCCR, Tropic) keep showing up in adjacent threads and a clear "not in this spec" is worth confirming.

## Bigger Picture

This is the largest single bundle the platform has seen. It sits on top of specs 014 (`IObjectStorage`), 015 (multi-currency), 016 (`Group` + `UserGroupMembership` + `AdminAuditEvent`), 017 (admin dashboard tiles + capability cards), 018 (Funding Agreement PDF), and 019 (Programa Semilla brand + sponsor strip), and pivots them all toward the *acompañamiento* identity that the May-13 session asked for. The `Process` aggregate is the structural keystone — without it, the per-Process stage-override (US4), the Plantilla snapshot (US1), the cascading admin filter (FR-034 / US6), and the supplier admin's Process filter (US3) all collapse.

Two patterns are worth noticing for a reviewer:

- **Copy-on-assign snapshot** (`ProcessPlantilla`). The spec stores `ImpactTemplateIdsCsv` as a denormalised CSV on the snapshot deliberately so that deleting a base `ImpactTemplate` never corrupts a Process that was running with it. See [research.md R-6](research.md#r-6--impact-value-object-on-application) and [data-model.md ProcessPlantilla](data-model.md#processplantilla). This is a deliberate trade against EF-level relational hygiene; whether it's the right call depends on how often base templates change.
- **Single soft-delete predicate** (`IApplicationQueryFilter.ExcludeDeleted`). The deleted-still-active defect (US8, [SC-011](spec.md#measurable-outcomes)) is being closed with one helper plus a structural test that uses reflection to forbid future projections from bypassing it. This is a recurrence-prevention shape (R-10) rather than a localised bug fix.

The plan's posture on "no production data" ([NFR-001](spec.md#non-functional-requirements)) is load-bearing: it allows dropping `Item.ImpactId` outright with no backfill, and seeding *Migración inicial* Process to absorb existing Groups. If that assumption ever turns out to be wrong in any deployment slice, the schema cutover blows up.

---

## Spec Review Guide (30 minutes)

### Understanding the approach (8 min)

Read [spec.md User Story 1](spec.md#user-story-1--annual-program-cycle-administration-priority-p1) and [User Story 2](spec.md#user-story-2--applicant-submits-an-application-end-to-end-on-the-new-flow-priority-p1) for the load-bearing structural moves, then [plan.md Project Structure](plan.md#project-structure) and [data-model.md Relationships](data-model.md#relationships) for how those land in code.

- Does the `Process → Group → Applicant → Application` chain in [data-model.md Relationships](data-model.md#relationships) match how stakeholders described "Crocus 2025" / "Nexo 2026" in the meeting?
- Is the *single-shot delivery* posture in [plan.md Summary](plan.md#summary) acceptable, or should this be split into two or three increments (e.g. structural moves first, then forms hardening + landing)?
- Is the no-production-data assumption ([NFR-001](spec.md#non-functional-requirements)) firm enough to justify the outright `Item.ImpactId` drop in [tasks.md T014](tasks.md#phase-2-foundational-blocking-prerequisites)?

### Key decisions that need your eyes (12 min)

**One Plantilla per Process** ([research.md OQ-1](research.md#oq-1--plantilla-assignment-cardinality-per-process))

Cardinality was chosen as strict one-to-one. The DB shape — `UNIQUE` on `ProcessPlantillas.ProcessId` — can be relaxed later, but every UX surface (assignment dropdown, Process detail page) assumes one snapshot.

- Is there any in-flight initiative that would want a Process to run multiple Plantillas concurrently (e.g. a Process scoping a *Capital Crocus* and a *Capital Nexo* track simultaneously)? If yes, the spec needs an early opt-out path.

**Snapshot stores `ImpactTemplateIdsCsv` as denormalised CSV** ([data-model.md ProcessPlantilla](data-model.md#processplantilla))

This sidesteps the corruption-via-delete problem but loses EF-level relational integrity. The rationale is "snapshot survives base-template deletion".

- Does this break reporting paths that today JOIN through `ImpactTemplate.Id`? If reports do JOIN through templates, a snapshot row with a CSV pointer to a deleted ImpactTemplate has to render *something*.

**Stage-expiry granularity per-Process only** ([research.md OQ-3](research.md#oq-3--stage-expiry-override-granularity))

Per-Plantilla overrides deferred. The current shape composes platform default → Process override only.

- Will US4's *facturación = 45 days for Crocus 2025* example ever need to differ between two Application kinds inside the same Process? If yes, the current shape locks that future option out.

**PublicCode template-field swap on the Funding Agreement PDF** ([research.md OQ-4](research.md#oq-4--publiccode-on-legacy-funding-agreement-pdf-spec-018))

The numeric reference is removed from the PDF template (legacy archived PDFs are not rewritten). Footnote dual-display was rejected to keep [SC-005](spec.md#measurable-outcomes) at 100%.

- For accountants reconciling old + new PDFs side-by-side, is the lost numeric continuity acceptable? Confirm with stakeholders before the swap lands in [tasks.md T155](tasks.md#phase-11-polish--cross-cutting).

**Single hosted background service for stage-expiry reminders** ([research.md R-2](research.md#r-2--stage-expiry-hosted-background-service))

Hourly cadence, in-process `IHostedService`, `RemindersSentMask` bitfield to enforce at-most-once per Application. The alternative (Hangfire / Quartz) was rejected per NFR-005.

- At seed scale (low-thousands Applications), is hourly iteration tight enough to honor the ±1h SC-008 granularity? At what scale does the in-process pattern stop scaling, and is that scale beyond 021's horizon?

### Areas where I'm less certain (5 min)

- [data-model.md Application](data-model.md#application): The spec says `ImpactTemplateId` is "nullable until applicant picks it on first save", but US2 AC also calls for *Impact step first*. I read this as "domain-required for `>= Submitted`, optional at the schema level" — but the relaxation feels brittle if a future code path persists a draft without calling `SetImpact()`.
- [tasks.md T154](tasks.md#phase-10-user-story-8--deleted-applications-no-longer-surface-as-active-p3) says *"Confirm Application soft-delete column (DeletedAt) exists; if absent, add via schema delta in T013"*. That's an instruction-time conditional in the task list — preferable to resolve before implementation starts (look up the current `dbo.Applications.sql` once, then either drop the conditional or commit to the schema delta).
- [research.md R-3](research.md#r-3--passwordresettoken--token-generation) keeps the ASP.NET Identity token provider AND adds a `PasswordResetTokens` table for single-use enforcement. That's double-bookkeeping; it works, but a reviewer with Identity expertise should sanity-check whether the table is necessary or whether `AspNetUserTokens` can carry the single-use marker.
- [contracts/applicant-routes.md](contracts/applicant-routes.md) defines `/Applications/{publicCode}/Edit` but [plan.md](plan.md) and [tasks.md T094](tasks.md#phase-4-user-story-2--applicant-submits-end-to-end-on-new-flow-p1) implies the internal numeric `Id` remains primary key. Confirming the route-binding flips on `PublicCode` (not `Id`) will affect dozens of existing `Url.Action(...)` call sites — not explicitly enumerated.

### Risks and open questions (5 min)

- If the *Migración inicial* Process seed in [tasks.md T020](tasks.md#phase-2-foundational-blocking-prerequisites) fails on a fresh container (e.g. on an environment that already had Groups from spec 016), will every subsequent `Application.GroupId` foreign-key path still resolve? The seed is idempotent, but its ordering relative to spec-016 data must be confirmed.
- The `ForbiddenStringsCrawler` ([research.md R-8](research.md#r-8--forbidden-string-ci-assertion)) is asserted in [tasks.md T087](tasks.md#tests), [T141](tasks.md#tests-1), and [T163](tasks.md#phase-11-polish--cross-cutting) — but its surface list is implicit ("every applicant-facing surface"). Is there a maintained registry of those surfaces, or does the crawler walk a hard-coded list that could drift as new views are added?
- [FR-007](spec.md#requirements) names *SupplierAdmin* as denied on "Applications, Users, Processes, Reports, Groups". [tasks.md T107](tasks.md#implementation-2) extends the list to include AdminCurrencies / AdminExchangeRates / AdminLegacyQuotations / AdminController. Is the union complete? A new admin controller introduced by spec 022+ would need a manual `[SupplierAdminDenied]` annotation — should this be a default-deny via convention instead?
- The plan ships **109 parallel-marked tasks** ([tasks.md Parallel opportunities](tasks.md#parallel-opportunities)) across 11 phases. Realistically, how many concurrent agents can a single developer + one Spec Guardian sustain without re-introducing the merge-conflict patterns that bit specs 011/017?
- Stage-expiry override on a `ProcessPlantilla` is one direction; reverting it sets the column to NULL ([data-model.md Process](data-model.md#process)). Audit-event payload for `ProcessStageWindowOverridden` carries `days: int | null` ([contracts/audit-events.md](contracts/audit-events.md)). Is that null-as-"revert-to-default" understood by the admin audit reader (spec 016 `IAdminAuditEventCopyProvider`)?

---
*Full context in linked [spec](spec.md) and [plan](plan.md).*
