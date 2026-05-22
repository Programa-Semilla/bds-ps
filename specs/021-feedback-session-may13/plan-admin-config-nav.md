# Increment Plan: US11 — System-configuration sidebar discoverability

**Feature:** 021-feedback-session-may13 (increment on an already-shipped spec)
**Spec scope:** User Story 11, FR-043, SC-020 (closes the OQ-12 Configuration clause)
**Created:** 2026-05-22
**Status:** Planned

> Sibling of US10 (`plan-impact-template-nav.md`) — identical root cause, identical fix shape.
> That plan's Technical Context, Constitution Check, and design rationale apply verbatim; only the
> target surface differs. This plan records the deltas. US1–US10 artifacts are shipped and MUST NOT
> be regenerated.

## Summary

`/Admin/Configuration` (admin-editable `SystemConfiguration` key/value rows) already has working
read/update CRUD (`AdminController.Configuration` GET `:142` / POST `:162`) and a *"Configuración del
sistema"* dashboard capability card (`AdminDashboardProjection.cs:128`, *Operaciones* section). The
US1 Process-pivot rebuilt the sidebar and dropped its direct entry. This increment adds one
Admin-only sidebar entry + E2E coverage. **No schema, no domain, no controller, no new view.**

## Design

### Sidebar entry (`src/FundingPlatform.Web/Views/Shared/_Layout.cshtml`, `adminEntries`)

Append after `legacy-quotations` (Operaciones-tail placement, matching the dashboard card's section):

```csharp
new("legacy-quotations", "Cotizaciones Pendientes", "/Admin/LegacyQuotations", "ti ti-history", new[] { "Admin" }),
new("system-config", "Configuración del sistema", "/Admin/Configuration", "ti ti-adjustments", new[] { "Admin" }),
```

- **Slug** `system-config` ⇒ stable testid `sidebar-entry-system-config` (FR-043).
- **Label** *"Configuración del sistema"* — matches the dashboard card + `UiCopy.SystemConfiguration`.
- **Icon** `ti ti-adjustments` — deliberately distinct from the admin **section header**'s
  `ti ti-settings` (`_Layout.cshtml:28`) so the child entry is not visually confused with its parent.
- **Roles** `new[] { "Admin" }` — SupplierAdmin-only variant never renders `adminEntries` (AC-3 free).

## Test Plan (E2E NON-NEGOTIABLE — real-journey rule)

New file `tests/FundingPlatform.Tests.E2E/.../AdminConfigNavTests.cs` (mirrors `ImpactTemplateNavTests`,
`AssignRoleAsync` login path):

1. **Sidebar → config (SC-020, AC-1/AC-2):** Admin logs in → assert
   `[data-testid="sidebar-entry-system-config"]` visible with `href="/Admin/Configuration"` → click it
   (no `Goto`) → assert URL `/Admin/Configuration` and the *"Configuración del sistema"* heading
   (`UiCopy.SystemConfiguration`) renders.
2. **SupplierAdmin-only sidebar lacks it (FR-043, AC-3):** SupplierAdmin-only user → `/Admin/Suppliers`
   → assert `sidebar-supplier-admin-variant` visible AND `sidebar-entry-system-config` count == 0.

No unit/integration tests — declarative nav entry only.

## Ordered Tasks

1. Add the `system-config` entry to `adminEntries` in `_Layout.cshtml`.
2. E2E `AdminConfigNavTests` (scenarios 1–2).
3. Full E2E suite green (delivery bar, NFR-004 / SC-016) → STAMP.
