// Spec 021 — see specs/021-feedback-session-may13/tasks.md T145/T146 and
// contracts/public-routes.md (Public landing) / spec FR-031.

namespace FundingPlatform.Web.ViewModels.Admin;

/// <summary>
/// Spec 021 / US7 / T146 / FR-031 — model for the admin upload form. Each
/// nullable property carries the current <c>ObjectKey.Value</c> persisted on
/// the matching <c>SystemConfiguration</c> row (or null if the slot is empty).
/// The view derives "is configured?" from null-ness only — it never tries to
/// stream the file size here because that would require a round-trip to
/// <see cref="FundingPlatform.Application.Abstractions.Storage.IObjectStorage"/>
/// on every page render.
/// </summary>
public sealed record AdminPublicLandingIndexViewModel(
    string? ReglamentoStorageKey,
    string? EjemploStorageKey);
