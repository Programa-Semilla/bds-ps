# Data Model — Spec 017 Admin UX/UI Facelift

**Branch**: `017-admin-ux-facelift` · **Date**: 2026-05-08

> **Schema delta: NONE.** Per FR-027 and SC-016, this spec adds zero rows / columns / indexes / constraints to `src/FundingPlatform.Database/`. Every projection is query-time aggregation against existing aggregates.

## Application-layer entities (DTOs / view models)

These are NEW types at the Application layer. None are persisted; all are computed per request.

### `AdminDashboardDto`

Top-level projection result for `/Admin`. Composes the entire dashboard.

| Field | Type | Source | Notes |
|---|---|---|---|
| `Kpis` | `AdminDashboardKpis` | sub-projection | 4 action KPIs |
| `Sections` | `IReadOnlyList<CapabilitySection>` | static template-time | exactly 3 sections, 9 cards |
| `RecentEvents` | `IReadOnlyList<AdminEvent>` | `IAdminAuditEventReader` | 0–5 entries; empty list when feed is hidden |
| `FeedVisible` | `bool` | derived | true iff `RecentEvents.Count > 0` |

### `AdminDashboardKpis`

| Field | Type | Source | Failure mode |
|---|---|---|---|
| `PendingSuppliers` | `int` | `ISupplierRepository` count where `VerificationStatus = PendingReview` | per R2: `0` + WARN log |
| `PendingLegacyQuotations` | `int` | `AdminLegacyQuotationsService` (or its repo equivalent) | per R2: `0` + WARN log |
| `AgingApplications` | `int` | spec-010 aging predicate + `AgingThresholdDays` config | per R2: `0` + WARN log |
| `ActiveUsers` | `int` | non-sentinel Active user count via `IUserStoreReader` | per R2: `0` + WARN log; sentinel exclusion per spec-009 FR-019 |

Each KPI carries a deep-link URL field (string), produced by the projection so the view layer doesn't compute URLs:
- `PendingSuppliers` → `/Admin/Suppliers?status=PendingReview`
- `PendingLegacyQuotations` → `/Admin/LegacyQuotations`
- `AgingApplications` → `/Admin/Reports/Aging`
- `ActiveUsers` → `/Admin/Users?status=Active`

### `CapabilitySection`

| Field | Type | Notes |
|---|---|---|
| `HeaderLabel` | `string` | "Usuarios y acceso" / "Catálogo" / "Operaciones" |
| `HeaderSlug` | `string` | `users-access` / `catalog` / `operations` (data-testid) |
| `Cards` | `IReadOnlyList<CapabilityCard>` | 2 / 4 / 3 cards per section |

### `CapabilityCard`

| Field | Type | Notes |
|---|---|---|
| `Slug` | `string` | stable English testid (e.g. `users`, `groups`, `suppliers`, `currencies`, `exchange-rates`, `impact-templates`, `reports`, `legacy-quotations`, `system-config`) |
| `Label` | `string` | es-CR display label (matches sidebar entry copy) |
| `Description` | `string` | one-line voice-guide-compliant es-CR copy |
| `Icon` | `string` | Tabler icon class (e.g. `ti ti-users`) |
| `CtaUrl` | `string` | navigation target |
| `CtaLabel` | `string` | "Administrar usuarios" / "Administrar grupos" / etc. |

### `AdminEvent`

Activity feed entry. Projected from `AdminAuditEvent` rows (last 30 days, top 5 by `OccurredAt` desc).

| Field | Type | Source | Notes |
|---|---|---|---|
| `OccurredAt` | `DateTimeOffset` | `AdminAuditEvent.OccurredAt` | rendered as "hace X minutos / horas / días" |
| `ActorDisplayName` | `string` | `IUserStoreReader.GetDisplayName(ActorUserId)` | sentinel never appears as actor |
| `Copy` | `string` | `IAdminAuditEventCopyProvider.Format(action, targetType)` | e.g. "creó el grupo Norte" |
| `DeepLinkUrl` | `string?` | computed by the projection | null when target is deleted (e.g. `group.delete`) |

## New Application-layer interfaces (services / providers)

### `IAdminDashboardProjection`

```csharp
namespace FundingPlatform.Application.Services;

public interface IAdminDashboardProjection
{
    Task<AdminDashboardDto> GetAsync(CancellationToken ct);
}
```

Single public method. Internal sub-projections are private. Pattern mirrors `IApplicantDashboardProjection` (`src/FundingPlatform.Application/Services/ApplicantDashboardProjection.cs`).

### `IAdminAuditEventReader`

```csharp
namespace FundingPlatform.Application.Services;

public interface IAdminAuditEventReader
{
    Task<IReadOnlyList<AdminAuditEvent>> GetRecentAsync(int take, TimeSpan window, CancellationToken ct);
}
```

Reads from `AdminAuditEvent` (spec 016 entity). Implementation lives in Infrastructure (`AdminAuditEventReader : IAdminAuditEventReader` over `DbContext.AdminAuditEvents`).

### `IAdminAuditEventCopyProvider`

```csharp
namespace FundingPlatform.Application.Services;

public interface IAdminAuditEventCopyProvider
{
    string Format(string action, string targetType, string? payloadJson);
}
```

es-CR copy provider; the only seam for future action-vocabulary additions. Initial mappings per R5.

### `IUserStoreReader` (extension)

`GetActiveUserCountAsync(CancellationToken ct) → int` — non-sentinel Active users. If a similar extension already exists from spec 009, reuse; otherwise add a method on the existing reader.

## Existing entities consumed (no changes)

| Entity / Service | Spec | Used for |
|---|---|---|
| `Supplier` (`SupplierVerificationStatus`) | 003, 013 | Pending-suppliers KPI |
| `Application` (`AgingThresholdDays` config) | 001, 010 | Aging-applications KPI |
| `User` / `IUserStore` (sentinel exclusion) | 009 | Active-users KPI |
| `Currency` / `ExchangeRate` | 015 | Catalog cards (links only) |
| `LegacyQuotation` / `AdminLegacyQuotationsService` | 015 | Pending-legacy-quotations KPI |
| `Group` / `UserGroupMembership` | 016 | Capability card target only |
| `AdminAuditEvent` | 016 | Activity feed source |
| `ImpactTemplate` / `SystemConfiguration` | pre-009 | Capability card targets |

## Schema delta (none)

```
$ git diff --stat src/FundingPlatform.Database/
(empty — SC-016 must hold)
```

## State transitions

None. This spec is read-only at the data layer. No existing entity gains new states or transitions.

## Validation rules

None at the domain layer. View-layer rules:
- Capability section assignment is fixed at template time (FR-004); not data-driven.
- Activity feed window is exactly 30 days (FR-024); top-5 by `OccurredAt` desc.
- KPI counts are non-negative integers; failures coerce to `0` per R2.

## Test data needs

E2E fixtures per SC-002 reference cases:
1. **Zero-of-everything**: one Admin user, no Suppliers, no LegacyQuotations, no aging Applications, no Groups, no AdminAuditEvents.
2. **Mixed mid-state**: a few PendingReview Suppliers, a few open LegacyQuotations, some aging Applications, several active Users (including a Reviewer + an Applicant), 1–2 recent AdminAuditEvents.
3. **All-thresholds-tripped**: PendingReview Suppliers > 10, LegacyQuotations > 10, aging > 10, AdminAuditEvents > 5 (verifies "top 5" cap).
4. **Pre-existing prod-like dataset**: the standard demo seed (existing `IdentityConfiguration` + a few Suppliers + Applications). Validates "real-world" appearance.

Fixtures are seeded by the E2E `AspireFixture` and tagged so each test selects the right scenario.

## Quickstart

See `quickstart.md` for the Phase 1 walk-through of building, running, and validating the dashboard against these fixtures.
