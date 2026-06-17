namespace FundingPlatform.Web.Models;

// Spec 037 D8 / FR-020 — reusable kebab actions component for admin list rows.
// A visible primary action (Editar) plus a "⋯" dropdown that wraps the row's
// remaining actions. Each action keeps its exact route/verb/antiforgery/testid/
// data-confirm wiring (the records below carry that wiring verbatim); only the
// presentation moves into the dropdown.

/// <summary>One entry inside the kebab dropdown. Renders as a GET link
/// (<see cref="Url"/> set) or a POST form+button (<see cref="FormController"/> +
/// <see cref="FormAction"/> set). Confirm fields, when present, emit the spec-024
/// <c>data-confirm-*</c> modal wiring on the button (with an optional native
/// <c>onsubmit</c> fallback for graceful degradation — NFR-004).</summary>
public sealed record RowActionItem(
    string Label,
    string TestId,
    string? Url = null,
    string? FormController = null,
    string? FormAction = null,
    object? FormRouteValues = null,
    string? Icon = null,
    bool IsDanger = false,
    string? ConfirmTitle = null,
    string? ConfirmBody = null,
    string? ConfirmLabel = null,
    string ConfirmVariant = "destructive",
    string? FormOnSubmit = null);

/// <summary>Model for <c>Views/Shared/Components/_RowActionsMenu.cshtml</c>.
/// <paramref name="MenuId"/> keys the kebab toggle testid
/// (<c>row-actions-menu-{MenuId}</c>) so E2E can open the menu. When
/// <paramref name="Items"/> is empty the kebab is not rendered (Edit-only row).</summary>
public sealed record RowActionsMenuViewModel(
    string MenuId,
    IReadOnlyList<RowActionItem> Items,
    string? EditUrl = null,
    string EditLabel = "Editar",
    string EditIcon = "ti ti-edit",
    string EditTestId = "row-action-edit",
    string MenuLabel = "Más acciones");
