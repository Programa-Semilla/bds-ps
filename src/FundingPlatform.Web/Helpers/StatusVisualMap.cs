using FundingPlatform.Domain.Enums;
using FundingPlatform.Web.Models;

namespace FundingPlatform.Web.Helpers;

public static class StatusVisualMap
{
    public static StatusVisual For(ApplicationState s) => s switch
    {
        ApplicationState.Draft => new StatusVisual("bg-secondary", "ti ti-pencil", "Borrador"),
        ApplicationState.Submitted => new StatusVisual("bg-info", "ti ti-send", "Enviada"),
        ApplicationState.UnderReview => new StatusVisual("bg-primary", "ti ti-eye", "En revisión"),
        ApplicationState.Resolved => new StatusVisual("bg-success", "ti ti-circle-check", "Resuelta"),
        ApplicationState.AppealOpen => new StatusVisual("bg-warning", "ti ti-alert-triangle", "Apelación abierta"),
        ApplicationState.ResponseFinalized => new StatusVisual("bg-info", "ti ti-checks", "Respuesta finalizada"),
        ApplicationState.AgreementExecuted => new StatusVisual("bg-success", "ti ti-file-check", "Convenio ejecutado"),
        _ => throw new ArgumentOutOfRangeException(nameof(s), s, "Unhandled ApplicationState"),
    };

    public static StatusVisual For(ItemReviewStatus s) => s switch
    {
        ItemReviewStatus.Pending => new StatusVisual("bg-secondary", "ti ti-clock", "Pendiente"),
        ItemReviewStatus.Approved => new StatusVisual("bg-success", "ti ti-check", "Aprobado"),
        ItemReviewStatus.Rejected => new StatusVisual("bg-danger", "ti ti-x", "Rechazado"),
        ItemReviewStatus.NeedsInfo => new StatusVisual("bg-warning", "ti ti-help-circle", "Requiere información"),
        _ => throw new ArgumentOutOfRangeException(nameof(s), s, "Unhandled ItemReviewStatus"),
    };

    public static StatusVisual For(AppealStatus s) => s switch
    {
        AppealStatus.Open => new StatusVisual("bg-warning", "ti ti-alert-triangle", "Abierta"),
        AppealStatus.Resolved => new StatusVisual("bg-success", "ti ti-circle-check", "Resuelta"),
        _ => throw new ArgumentOutOfRangeException(nameof(s), s, "Unhandled AppealStatus"),
    };

    public static StatusVisual For(SignedUploadStatus s) => s switch
    {
        SignedUploadStatus.Pending => new StatusVisual("bg-secondary", "ti ti-clock", "Pendiente"),
        SignedUploadStatus.Approved => new StatusVisual("bg-success", "ti ti-check", "Aprobada"),
        SignedUploadStatus.Rejected => new StatusVisual("bg-danger", "ti ti-x", "Rechazada"),
        SignedUploadStatus.Superseded => new StatusVisual("bg-secondary", "ti ti-arrow-back-up", "Reemplazada"),
        SignedUploadStatus.Withdrawn => new StatusVisual("bg-secondary", "ti ti-arrow-back-up", "Retirada"),
        _ => throw new ArgumentOutOfRangeException(nameof(s), s, "Unhandled SignedUploadStatus"),
    };

    public static StatusVisual For(AdminUserStatus s) => s switch
    {
        AdminUserStatus.Active => new StatusVisual("bg-success", "ti ti-circle-check", "Activo"),
        AdminUserStatus.Disabled => new StatusVisual("bg-secondary", "ti ti-ban", "Inhabilitado"),
        _ => throw new ArgumentOutOfRangeException(nameof(s), s, "Unhandled AdminUserStatus"),
    };

    public static StatusVisual For(AdminUserRole r) => r switch
    {
        AdminUserRole.Applicant => new StatusVisual("bg-info", "ti ti-user", "Solicitante"),
        AdminUserRole.Reviewer => new StatusVisual("bg-primary", "ti ti-eye", "Revisor"),
        // Spec 021 / FR-007 — distinct chip styling so the Users list reads at a glance.
        AdminUserRole.Auditor => new StatusVisual("bg-azure", "ti ti-clipboard-check", "Auditor"),
        AdminUserRole.Admin => new StatusVisual("bg-purple", "ti ti-shield-lock", "Administrador"),
        _ => throw new ArgumentOutOfRangeException(nameof(r), r, "Unhandled AdminUserRole"),
    };
}
