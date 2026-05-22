namespace FundingPlatform.Web.Models;

public sealed record ConfirmDialogViewModel(
    string Id,
    string Title,
    string IrreversibilityRationale,
    string ConfirmLabel,
    string CancelLabel = "Cancelar", // Spec 024 — es-CR default (was "Cancel"); FR-007/FR-010.
    ActionClass ConfirmClass = ActionClass.Destructive,
    string FormController = "",
    string FormAction = "",
    object? FormRouteValues = null);
