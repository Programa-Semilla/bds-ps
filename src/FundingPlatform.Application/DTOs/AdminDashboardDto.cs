namespace FundingPlatform.Application.DTOs;

/// <summary>
/// Spec 017 / US1 — top-level projection result for the admin dashboard.
/// Composed by <c>IAdminDashboardProjection</c> per request; never persisted.
/// </summary>
public sealed record AdminDashboardDto(
    AdminDashboardKpis Kpis,
    IReadOnlyList<CapabilitySection> Sections,
    IReadOnlyList<AdminEvent> RecentEvents,
    bool FeedVisible);

/// <summary>
/// Spec 017 / FR-002 — four action KPIs surfaced on the admin dashboard.
/// Each carries a precomputed deep-link URL so the view layer does not build paths.
/// Per R2, every count degrades to <c>0</c> on sub-projection failure.
/// </summary>
public sealed record AdminDashboardKpis(
    int PendingSuppliers,
    string PendingSuppliersUrl,
    int PendingLegacyQuotations,
    string PendingLegacyQuotationsUrl,
    int AgingApplications,
    string AgingApplicationsUrl,
    int ActiveUsers,
    string ActiveUsersUrl);

/// <summary>
/// Spec 017 / FR-004 — fixed-template grouping of capability cards.
/// Section assignment is template-time, not data-driven.
/// </summary>
public sealed record CapabilitySection(
    string HeaderLabel,
    string HeaderSlug,
    IReadOnlyList<CapabilityCard> Cards);

/// <summary>
/// Spec 017 / FR-005 — single admin capability tile (icon + label + description + CTA).
/// Slug is a stable English testid even when the visible label is Spanish.
/// </summary>
public sealed record CapabilityCard(
    string Slug,
    string Label,
    string Description,
    string Icon,
    string CtaUrl,
    string CtaLabel);

/// <summary>
/// Spec 017 / US7 — single activity-feed entry projected from <c>AdminAuditEvent</c>.
/// <c>DeepLinkUrl</c> is null when the target was deleted (e.g. <c>group.delete</c>).
/// </summary>
public sealed record AdminEvent(
    DateTimeOffset OccurredAt,
    string ActorDisplayName,
    string Copy,
    string? DeepLinkUrl);
