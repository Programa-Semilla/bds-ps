# Implementation Plan: Auditor Role + Provider Regulatory Compliance Model

**Branch**: `038-auditor-provider-compliance` | **Date**: 2026-06-17 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `specs/038-auditor-provider-compliance/spec.md`

## Summary

Foundation slice (A) of feedback round 3. Rename the `SupplierAdmin` role to **`Auditor`** (rename the
existing `AspNetRoles` row so members carry over), replace the four provider compliance **booleans** with
three **nullable enumerated statuses** (Hacienda/CCSS/SICOP, stored as `TINYINT` via the established
`HasConversion<byte>()` pattern; the verbatim Spanish labels live in a display resolver), remove the
electronic-invoice control everywhere, add a `IsPmeOrPyme` flag and a `HasWarning`/`WarningNote` pair, record
every regulatory/PME/warning change to the existing generic `AdminAuditEvent` trail (new `supplier.*` action
constants), keep per-field "last reviewed" metadata on the `Supplier` row for a freshness display, add a
"reviewed — no change" re-authorize action, and email all auditors when a provider is created — sending via
the **allowlist-wrapped Notifications `IEmailSender`** (the direct-send Abstractions path is NOT allowlisted).
A `RowVersion` is added to `Supplier` for optimistic concurrency. Recommendation scoring, the auditor
workflow stage, freshness *enforcement*, and the Hacienda API job are explicitly deferred to slices B/C/D.

## Technical Context

**Language/Version**: C# / .NET 10.0, ASP.NET MVC, EF Core 10
**Primary Dependencies**: ASP.NET Identity (roles), Syncfusion (unaffected), Tabler.io (UI), existing
`AdminAuditEvent` + `IAdminAuditEventWriter`, Notifications `IEmailSender` (+ `RecipientAllowlistFilter`),
Playwright (E2E + `MailCaptureClient`/smtp4dev). **No new managed dependencies.**
**Storage**: SQL Server via dacpac (`FundingPlatform.Database`). Changes: alter `dbo.Suppliers`
(drop 4 BIT compliance columns; add 3 status TINYINTs + per-field reviewed-at/by/source + `IsPmeOrPyme` +
`HasWarning` + `WarningNote` + `RowVersion`); replace post-deploy `03_SeedSupplierAdminRole.sql` with an
idempotent rename-or-create of the `Auditor` role.
**Testing**: Unit (domain methods + display/freshness helpers), Integration (real DB: compliance edit +
audit rows + re-review timestamp refresh), E2E (Playwright, per user story; mail capture for US4).
**Target Platform**: Linux container (Aspire-orchestrated), es-CR culture.
**Project Type**: Web application (existing 4-layer Clean Architecture solution).
**Performance Goals**: N/A beyond existing admin-screen norms; freshness display is a direct column read.
**Constraints**: Schema-first (dacpac, no EF migrations); greenfield/no-backfill; reuse existing audit +
email seams; capability parity for the renamed role; verbatim Spanish enum labels.
**Scale/Scope**: ~50 `SupplierAdmin` reference sites to rename; one `Supplier` aggregate + EF config + dacpac
table; one new Application service + one notifier; admin supplier Detail screen rework (checkboxes → selects +
warning + freshness + re-review); reviewer review surface gains warning + freshness.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-checked after Phase 1 design.*

| Principle | Compliance |
|---|---|
| **I. Clean Architecture** | New status/source enums + `Supplier` behavior in **Domain**; `ISupplierComplianceService` + `IProviderCreatedNotifier` + DTOs in **Application**; EF config, service impls, notifier in **Infrastructure**; controllers/views/VMs in **Web**. Dependencies point inward. ✅ |
| **II. Rich Domain Model** | Compliance/warning/review logic lives on `Supplier` (`ApplyRegulatoryEdit`, `ConfirmRegulatoryReviewed`, `SetPmeOrPyme`, `SetWarning`) returning change records; services orchestrate audit + commit. No anemic drift. ✅ |
| **III. E2E (NON-NEGOTIABLE)** | Each of US1–US4 gets Playwright coverage (golden + key error); US4 asserts mail capture via smtp4dev. Page Object Model. ✅ |
| **IV. Schema-First (dacpac)** | All schema via `.sql` edits; role migration via post-deploy script; no EF migrations / EnsureCreated. ✅ |
| **V. Spec-Driven** | spec → plan → tasks → impl. ✅ |
| **VI. Simplicity/YAGNI** | Reuse generic `AdminAuditEvent` (no new audit table); reuse Notifications `IEmailSender` (no new email infra); defer scoring/workflow/enforcement/API. The only additive primitive is `RowVersion` on `Supplier` (justified below). ✅ |

**Quality gates honored:** optimistic concurrency added (`RowVersion`); validation errors aggregated;
authorization preserved (Auditor scoped to supplier screens via the existing deny filter).

**Result:** PASS (initial and post-design). No violations requiring Complexity Tracking justification beyond
the two design notes below.

## Project Structure

### Documentation (this feature)

```text
specs/038-auditor-provider-compliance/
├── plan.md              # This file
├── research.md          # Phase 0 — decisions & rationale
├── data-model.md        # Phase 1 — entity + dacpac changes
├── quickstart.md        # Phase 1 — how to exercise the feature
├── contracts/           # Phase 1 — service/notifier interfaces + routes + enum value tables
│   └── interfaces.md
├── spec.md / REVIEW-SPEC.md / review_brief.md / checklists/
└── tasks.md             # Phase 2 (/speckit-tasks — NOT created here)
```

### Source Code (repository root) — touched paths

```text
src/FundingPlatform.Domain/
├── Entities/Supplier.cs                         # drop bools; add statuses+metadata+pme+warning+rowversion; new methods
├── Enums/HaciendaStatus.cs  CcssStatus.cs  SicopStatus.cs  RegulatoryReviewSource.cs  RegulatoryField.cs   # new
└── Entities/AdminAuditEvent.cs                  # new supplier.* action constants + TargetTypeSupplier

src/FundingPlatform.Application/
├── Suppliers/Compliance/ISupplierComplianceService.cs + command/result DTOs   # new
├── Suppliers/Notifications/IProviderCreatedNotifier.cs                          # new
└── Suppliers/RegulatoryStatusLabels.cs (display map) + ReviewFreshness helper   # new (or Web/Resources)

src/FundingPlatform.Infrastructure/
├── Services/SupplierComplianceService.cs        # mutate + audit + commit (mirrors CompanyAdministrationService)
├── Suppliers/ProviderCreatedNotifier.cs         # resolve auditors, render, send via Notifications IEmailSender
├── Persistence/Configurations/SupplierConfiguration.cs   # map new enums (HasConversion<byte?>), RowVersion, drop bools
├── Identity/IdentityConfiguration.cs            # roles array + demo user → Auditor
└── Services/CreateSupplierBranchHandler.cs      # fire best-effort notifier after SaveChanges

src/FundingPlatform.Database/
├── Tables/dbo.Suppliers.sql                     # column changes (drop 4 BIT, add statuses+metadata+pme+warning+rowversion)
└── PostDeployment/03_SeedSupplierAdminRole.sql  # → rename-or-create Auditor role (idempotent)

src/FundingPlatform.Web/
├── Filters/SupplierAdminOnlyAttribute.cs + SupplierAdminDeniedAttribute.cs   # role constant → "Auditor"
├── Controllers/**/*.cs                          # 13 [Authorize] + IsInRole checks + AssignRole seam → Auditor
├── Controllers/Admin/AdminSuppliersController.cs # Edit→service; new ConfirmReviewed action; warning fields
├── Models/AdminUserRole.cs + Helpers/StatusVisualMap.cs + AccountController role display   # Auditor
├── ViewModels/Admin/AdminSupplierDetailViewModel.cs   # statuses+pme+warning+freshness+rowversion
├── Views/Admin/Suppliers/Detail.cshtml          # checkboxes → selects + warning + freshness + re-review
├── Views/Emails/Suppliers/ProviderCreatedAuditor.cshtml   # new (text-template pattern)
├── Resources/AdminSuppliersResources.cs         # new es-CR strings + status labels
└── (review surface) supplier/quote render partial(s)   # warning banner + per-field freshness

tests/FundingPlatform.Tests.Unit|Integration|E2E   # domain/helpers; compliance+audit; US1–US4 Playwright
```

**Structure Decision**: Existing 4-layer solution; no new projects. New audited-mutation flow follows the
`CompanyAdministrationService`/`FundService` precedent (Application interface + Infrastructure impl that
writes the audit event and commits atomically in one `SaveChangesAsync`).

## Complexity Tracking

> Two design notes (not violations) recorded for reviewer visibility.

| Decision | Why | Alternative rejected because |
|---|---|---|
| Add `RowVersion` to `Supplier` | Constitution mandates optimistic concurrency for concurrent-edit-risk entities; multiple auditors (and slice D's Hacienda API job) will write the same provider row | `UpdatedAt`-only (current) gives silent last-write-wins; the spec's edge case assumed an OC token that does not yet exist |
| Reuse generic `AdminAuditEvent` for the regulatory trail (vs a dedicated `ProviderRegulatoryAuditEvent` table) | Matches the dominant repo pattern (`fund.*`/`company.*`); freshness *display* is served by per-field columns on `Supplier`, not by querying the trail; payload carries `{supplierId, field, oldValue, newValue, source, kind}` and `TargetId=supplierId` makes it queryable via the existing target index | A dedicated table is more ergonomic for rich prev/new columns but is premature for slice A; **slice D** may revisit if the Hacienda API job's automated actor (no `AspNetUsers` row) or a dedicated history page demands it (flagged in research.md) |
