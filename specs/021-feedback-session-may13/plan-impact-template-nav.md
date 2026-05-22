# Increment Plan: US10 — Impact-template sidebar discoverability

**Feature:** 021-feedback-session-may13 (increment on an already-shipped spec)
**Spec scope:** User Story 10, FR-042, SC-019, OQ-12
**Created:** 2026-05-22
**Status:** Planned

> This plan covers ONLY the US10 increment. The US1–US9 artifacts (`plan.md`, `research.md`,
> `data-model.md`, `contracts/`, `tasks.md`, `plan-us9-delete-withdrawal.md`) are shipped and MUST
> NOT be regenerated or altered.

## Summary

The impact-template admin CRUD surface (`/Admin/ImpactTemplates`) **already exists and works** — list
(`AdminController.ImpactTemplates`), create (`CreateTemplate`), edit (`EditTemplate`), and soft-delete
via the `IsActive` toggle inside the edit form. It is rendered as a *"Plantillas de impacto"*
capability card on the `/Admin` dashboard (`AdminDashboardProjection.cs:116`,
`_AdminDashboard.cshtml:84-92`). The gap is **discoverability only**: the persistent left sidebar,
rebuilt around `Process` in US1, has no direct entry, so an admin browsing by menu cannot find it.
This increment adds one Admin-only sidebar entry and one real-journey E2E test. **No schema, no
domain, no controller, no new view.**

## Technical Context

- **Language/stack:** C# 13 / .NET 10, ASP.NET MVC (Razor `_Layout.cshtml`). Unchanged.
- **No new managed dependencies** (NFR-005).
- **No schema change** — pure navigation. Target controller actions + views all pre-exist.
- **Existing surface (verified):**
  - List/Create/Edit: `src/FundingPlatform.Web/Controllers/AdminController.cs:32-139`
    (`[Authorize(Roles="Admin,SupplierAdmin")]` + `[SupplierAdminDenied]` — effectively Admin-only).
  - List view: `src/FundingPlatform.Web/Views/Admin/ImpactTemplates.cshtml`.
  - Dashboard card (stays): `AdminDashboardProjection.cs:116-117`.
- **Sidebar source:** `src/FundingPlatform.Web/Views/Shared/_Layout.cshtml`
  - `adminEntries` flat list (`:30-43`) — render path `:135-157`.
  - SupplierAdmin-only path renders the separate `supplierAdminEntries` (`:49-52`), so adding to
    `adminEntries` is **automatically** excluded from the SupplierAdmin-only sidebar (FR-042 / AC-3
    satisfied for free). Verify, do not add a second guard.
  - Each entry already emits `data-testid="sidebar-entry-@entry.Slug"` (`:151`), so the testid is
    a function of the slug — slug `impact-templates` ⇒ `sidebar-entry-impact-templates` (FR-042).
- **NEEDS CLARIFICATION:** none. OQ-12 (inline list-level deactivate toggle; Configuration nav
  parity) is explicitly deferred, not an implementation unknown.

## Constitution Check

| Principle | Status | Note |
|---|---|---|
| I. Clean Architecture | PASS | View-layer nav only; no business logic touched. |
| II. Rich Domain Model | PASS | No domain change. |
| III. E2E (NON-NEGOTIABLE) | PASS | Real-journey Playwright test: admin logs in, clicks the sidebar entry (no deep-link), lands on the list, creates a template. Negative: SupplierAdmin-only sidebar lacks the entry. |
| IV. Schema-First DB | PASS | Zero schema delta. |
| V. Spec-Driven Development | PASS | Derives from US10 / FR-042 / SC-019. |
| VI. Simplicity | PASS | One sidebar list entry + one E2E test. Smallest change that closes the gap. |

No violations; no Complexity Tracking entries.

## Design

### Sidebar entry (`src/FundingPlatform.Web/Views/Shared/_Layout.cshtml`, `adminEntries` `:30-43`)

Add one entry, placed immediately after `plantillas` (workflow adjacency: define impact templates →
attach to a Plantilla → assign to a Process):

```csharp
new("plantillas", "Plantillas", "/Admin/Plantillas", "ti ti-template", new[] { "Admin" }),
new("impact-templates", "Plantillas de impacto", "/Admin/ImpactTemplates", "ti ti-clipboard-data", new[] { "Admin" }),
new("users", "Usuarios", "/Admin/Users", "ti ti-users", new[] { "Admin" }),
```

- **Slug** `impact-templates` ⇒ stable testid `sidebar-entry-impact-templates` (FR-042, NFR-001).
- **Label** *"Plantillas de impacto"* — matches the dashboard card copy (cross-surface recognition).
- **Icon** `ti ti-clipboard-data` — deliberately distinct from `plantillas`' `ti ti-template` so the
  two related-but-different surfaces are visually disambiguated in the sidebar. (Tabler vendored set;
  no asset budget impact.)
- **Roles** `new[] { "Admin" }` — excludes Reviewer/Applicant via `IsEntryVisible`, and the
  SupplierAdmin-only variant never renders `adminEntries` at all (AC-3).

### No other production change

The dashboard card, controller, list view, and CRUD remain untouched. OQ-12 (list-level
Activar/Desactivar affordance; `/Admin/Configuration` sidebar parity) is out of scope.

## Test Plan (E2E NON-NEGOTIABLE — Constitution III, NFR-004, real-journey rule)

New file `tests/FundingPlatform.Tests.E2E/.../ImpactTemplateNavTests.cs` (reuse the admin login
helper used by other admin E2E specs; sentinel `admin@programa-semilla.test` / `Sentinel123!`):

1. **Sidebar → list → create (SC-019, AC-1/AC-2):** log in as Admin → assert
   `[data-testid="sidebar-entry-impact-templates"]` is visible in the rendered sidebar → click it →
   assert URL/landing is `/Admin/ImpactTemplates` and the *"Crear nueva plantilla"* affordance is
   present → click it, fill name + one parameter, submit → assert the new template row appears in the
   list. Drives the real menu journey end-to-end; no `page.Goto("/Admin/ImpactTemplates")` shortcut.
2. **SupplierAdmin-only sidebar lacks the entry (FR-042, AC-3):** log in as a SupplierAdmin-only user
   → assert `[data-testid="sidebar-entry-impact-templates"]` is **absent** (and the
   `sidebar-supplier-admin-variant` is present). If no SupplierAdmin-only seed user exists in the
   ephemeral fixture, fold this into an existing supplier-admin sidebar test or add the assertion
   there rather than duplicating fixture setup.

No unit/integration tests required — there is no new logic, only a declarative nav entry. The E2E
journey is the meaningful coverage.

## Ordered Tasks

1. Add the `impact-templates` entry to `adminEntries` in `_Layout.cshtml` (after `plantillas`).
2. E2E: `ImpactTemplateNavTests` scenario 1 (sidebar → list → create real journey).
3. E2E: scenario 2 (SupplierAdmin-only sidebar absence) — new test or assertion folded into the
   existing supplier-admin sidebar test.
4. Full E2E suite green (delivery bar, NFR-004 / SC-016) → STAMP.

## Open Questions (carried)

- **OQ-12:** Inline list-level *Activar / Desactivar* toggle on `/Admin/ImpactTemplates`, and
  `/Admin/Configuration` sidebar parity — both deferred; current CRUD is complete and the
  Configuration gap shares the same US1 root cause but is outside an impact-template-scoped story.
