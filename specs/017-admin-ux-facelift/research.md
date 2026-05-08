# Research — Spec 017 Admin UX/UI Facelift

**Branch**: `017-admin-ux-facelift` · **Date**: 2026-05-08

Resolves the planning-deferred decisions from `REVIEW-SPEC.md` and the assumptions in `spec.md` that need code-grounded confirmation before Phase 1.

## R1 — Pending-supplier source enum value

**Open thread (assumption + REVIEW-SPEC important)**: which enum value is "Pending suppliers"?

**Decision**: `SupplierVerificationStatus.PendingReview`.

**Evidence**:
- `src/FundingPlatform.Domain/Enums/SupplierVerificationStatus.cs` declares the enum (Draft / PendingReview / Verified / Rejected).
- `src/FundingPlatform.Web/Controllers/Admin/AdminSuppliersController.cs:41-44` already documents "Spec 013 FR-030: default filter on entry is `PendingReview`" and uses that value as the Suppliers-index default. The Pending-suppliers KPI inherits the same definition for cross-surface consistency (KPI count == Suppliers index default-filter row count).
- The KPI deep-link target becomes `/Admin/Suppliers?status=PendingReview` (matching the existing controller route shape).

**Rationale**: A single source of truth for "what is pending" — the dashboard KPI, the deep-link target, and the index default-filter all resolve to the same enum value. Eliminates "best available count" ambiguity flagged in REVIEW-SPEC.

**Alternatives considered**:
- A separate `SupplierStatus.Pending` enum (rejected — `SupplierVerificationStatus` is already the platform's status type and shipped in spec 013).
- `Draft + PendingReview` combined (rejected — Draft suppliers are applicant-side WIP, not awaiting admin; combining would inflate the KPI artificially).

## R2 — Pending-supplier failure mode

**Open thread (REVIEW-SPEC important + edge case)**: when the source query fails or returns null, what does the KPI tile render?

**Decision**: render the count as `0` with no error styling, and log a structured warning at WARN level. The "—" placeholder treatment is rejected.

**Rationale**:
- All four KPI sources are existing in-process repository queries against an in-cluster SQL Server — failure is a logic bug, not a network partition. Rendering `0` is consistent with "no pending suppliers right now"; an admin landing on the dashboard then opening Suppliers will see the actual rows (or actual emptiness) and the discrepancy is a clear bug signal.
- An "—" placeholder degrades the deep-link action (clicking would still navigate but the count would be opaque) and requires custom styling that diverges from `_KpiTile`'s contract.
- An error tile (red badge) would surface infra issues to admins who can't fix them and is alarming for a synchronous projection failure that should be impossible in healthy ops.

**Implication for FR-002 / FR-003**: KPI projection MUST swallow exceptions inside the projection layer (`AdminDashboardProjection`) and emit them as `0` + a WARN-level structured log entry tagged `AdminDashboardKpiProjectionFailed { kpi, reason }`. Tests cover this path with a fault-injecting fake.

## R3 — FR-015 implementation choice (sidebar grouping)

**Open thread (REVIEW-SPEC important + spec FR-015)**: clicking the "Administración" section header navigates to `/Admin`, OR a sub-entry "Panel" inside the section is the navigable item?

**Decision**: **Section header click target navigates to `/Admin`.** No "Panel" sub-entry.

**Rationale**:
- Mirrors how the existing top-level "Administración" entry works today (link to `/Admin`); the change is grouping the entries underneath, not adding a new entry.
- Adding a "Panel" sub-entry would force a redundant first sub-entry that mirrors the section header's target, and cost an additional `data-testid` + label + translation overhead.
- The section header double-duties as both a visual divider and the "go home" target. This is the same pattern Tabler.io's vertical navbar supports natively (`<a class="nav-link" data-bs-toggle="collapse">` with a `<a class="nav-link" href>` action target), and the existing `_Layout.cshtml` already uses simple `<a class="nav-link">` so the section header becomes an `<a>` rendering both the section label and the navigation target.

**E2E impact**: existing `data-testid="sidebar-entry-admin"` slug stays on the section header, preserving testid stability per FR-016.

**Alternative considered**: a "Panel" sub-entry was viable but adds noise. Documented as rejected in `implementation-notes.md`.

## R4 — Admin dashboard projection naming + placement

**Open thread (REVIEW-SPEC optional)**: name the projection in the spec or defer to plan?

**Decision (planning-pinned)**:
- Interface: `IAdminDashboardProjection` in `FundingPlatform.Application/Services/`.
- Implementation: `AdminDashboardProjection` in `FundingPlatform.Application/Services/`.
- DTO: `AdminDashboardDto` in `FundingPlatform.Application/DTOs/`.
- Pattern mirrors spec 011's `IApplicantDashboardProjection` / `ApplicantDashboardProjection` / `ApplicantDashboardDto` (`src/FundingPlatform.Application/Services/ApplicantDashboardProjection.cs`).

**Rationale**: A named projection at the Application layer keeps the controller thin (single `await projection.GetAsync(ct)` call), satisfies Constitution Principle I (Clean Architecture; Web depends on Application), and gives the test suite a single seam to fault-inject for R2's failure-mode test.

**Sub-projections** (each is a private method on `AdminDashboardProjection`, not separate services — keeps the public surface narrow):
- `GetPendingSupplierCountAsync()` — counts `SupplierVerificationStatus.PendingReview`.
- `GetPendingLegacyQuotationCountAsync()` — counts via `AdminLegacyQuotationsService` (or its projection equivalent; planning verifies during data-model authoring).
- `GetAgingApplicationCountAsync()` — reuses spec-010 aging predicate + `AgingThresholdDays` config.
- `GetActiveUserCountAsync()` — counts non-sentinel Active users (per spec-009 sentinel exclusion).

## R5 — Activity feed event copy mapping

**Open thread (FR-025)**: event copy format = "{actor} {action} {target}" with relative timestamp; action vocabulary drawn from existing `AdminAuditEvent` enum without expansion.

**Evidence**: `src/FundingPlatform.Domain/Entities/AdminAuditEvent.cs` declares 4 action constants:
- `group.create` → "creó el grupo"
- `group.rename` → "renombró el grupo"
- `group.delete` → "eliminó el grupo"
- `user.memberships.update` → "actualizó las membresías de"

**Decision**: an `IAdminAuditEventCopyProvider` (Application layer) maps `(action, targetType)` to es-CR phrasing. Voice-guide-compliant, second person addressing the user is N/A here (third-person past tense for the audit), no exclamation marks. Copy provider is the single seam for future action additions.

**Deep-link target**:
- `targetType=group` → `/Admin/Groups/{targetId}/Edit`
- `targetType=user` → `/Admin/Users/{targetId}/Edit`

If the target was deleted (e.g., `group.delete`), the deep-link is suppressed and the row renders the event copy with no link. Consistent with spec 011 `_EventTimeline` behavior for orphaned references.

## R6 — Sweep inventory grep audit (Phase 0 finding)

**Decision**: the planning artifact `ADMIN-SWEEP-CHECKLIST.md` enumerates each view in FR-008 with the seven swept criteria as check items. The grep audit at the start of implementation produces a `sweep-audit-2026-05-08.txt` artifact (not committed) listing existing violations per view, and tasks.md tasks reference specific lines.

**Rationale**: SC-007 demands a manual checklist walk; the checklist deliverable is what the manual walk uses.

## R7 — Existing partial inventory confirmation

**Evidence** (from `src/FundingPlatform.Web/Views/Shared/Components/`):
- spec 008 partials present: `_ActionBar`, `_ConfirmDialog`, `_DataTable`, `_DocumentCard`, `_EmptyState`, `_EventTimeline`, `_FormSection`, `_PageHeader`, `_StatusPill`.
- spec 010 partials present: `_KpiTile`, `_ReportSubTabs`.
- spec 011 partials present: `_ApplicantHero`, `_ApplicationCard`, `_ApplicationJourney`, `_ResourcesStrip`, `_ReviewerHero`, `_ReviewerQueueRow`, `_SigningCeremony`, `ConversionIndicator`, `MoneyDisplay`.
- `Html.Illustration("scene-key")` helper present at `src/FundingPlatform.Web/Helpers/IllustrationHelper.cs`.

**New partials added by this spec** (per FR-006):
- `_AdminDashboard.cshtml` — composes the dashboard layout (page header → KPI strip → 3 capability sections → optional activity feed).
- `_CapabilityCard.cshtml` — single capability tile (icon + label + description + CTA).

**No other new partials.** `_KpiTile` is re-templated in place (US6 + FR-022), not forked. `_ReportSubTabs` is re-templated in place (US6 + FR-021). `_EmptyState` already accepts an `IllustrationSceneKey` parameter (verified at `_EmptyState.cshtml:4-12`).

## R8 — Route normalization scope

**Decision** (planning-pinned per `Open Threads`): route normalization touches **route attributes only**, not class names or namespaces. Class names stay `AdminCurrenciesController` / `AdminExchangeRatesController` / `AdminLegacyQuotationsController`. Folder structure (`Controllers/Admin/`) unchanged.

**Rationale**: Class name renames cascade to namespaces, view folder paths (since views resolve by controller name minus "Controller"), and any DI registration keyed on type. Each cascade increases blast radius without delivering user-visible UX. The user-visible win is the URL; the developer cognitive-load win from class renaming is a nice-to-have we explicitly defer to a future "admin module reorganization" spec if/when it ships.

**Implementation mechanism**: each affected controller declares an explicit `[Route("Admin/Currencies")]` (etc.) attribute on the controller class, overriding the default conventional route. The default convention `[area:Admin]/[controller]/[action]` would otherwise map `AdminCurrenciesController` to `/Admin/AdminCurrencies`, which we explicitly want to break. View location expander (`AdminAreaViewLocationExpander.cs`) does NOT need changes because views resolve by controller name (still `AdminCurrencies`), not route.

## R9 — Reduced-motion contract scope

**Reuse**: spec 011 FR-015's `prefers-reduced-motion` contract in `tokens.css` already governs all motion in the catalog. Spec 017's KPI ticker (FR-002 + FR-022) is NEW motion at NEW host surfaces (admin dashboard, reports tabs) but uses the existing motion token (`--motion-slow`) and the existing JS ticker (whatever `_KpiTile` already uses for the reviewer queue per spec 011 FR-053). No new motion catalog entry is added; this spec consumes the existing catalog.

**Test mechanism**: a dedicated Playwright test runs the admin dashboard with `reducedMotion: 'reduce'` and asserts KPI counts render their final values immediately (no ticker animation observed). Same pattern as spec 011's reduced-motion test.

## R10 — Schema-unchanged proof

**Decision (locked)**: `git diff --stat` against `src/FundingPlatform.Database/` MUST be empty at PR open. SC-016 enforces.

**Tested via**: a one-line CI check or a `scripts/verify-no-database-changes.sh` script invoked from a final task. Same mechanism as spec 011 SC-018.

If a planning-phase or implementation-phase need surfaces, the plan-time escape is `/speckit-spex-evolve` per FR-027.

---

## Summary of resolved threads

| Thread | Resolution | Reference |
|---|---|---|
| Pending-supplier source enum | `SupplierVerificationStatus.PendingReview` | R1 |
| Pending-supplier failure mode | Render `0` + WARN log | R2 |
| FR-015 implementation choice | Section header click target → `/Admin` | R3 |
| Projection class naming | `IAdminDashboardProjection` / `AdminDashboardProjection` / `AdminDashboardDto` | R4 |
| Activity feed copy mapping | `IAdminAuditEventCopyProvider` for 4 actions | R5 |
| Sweep audit deliverable | `ADMIN-SWEEP-CHECKLIST.md` per FR-008 / SC-007 | R6 |
| Partial inventory | 2 new partials (`_AdminDashboard`, `_CapabilityCard`); rest re-templated in place | R7 |
| Route normalization scope | Attributes only; class names unchanged | R8 |
| Reduced-motion contract | Reuse spec 011 contract; no new catalog entry | R9 |
| Schema-unchanged enforcement | CI check + `git diff --stat` empty at PR | R10 |

All NEEDS CLARIFICATION items are resolved. Ready for Phase 1 design.
