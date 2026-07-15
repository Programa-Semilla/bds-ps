namespace FundingPlatform.Web.Resources;

/// <summary>
/// Spec 044 / US1 — localized (es-CR) strings for the admin "Ventanas de
/// recepción" card on the Process detail page. NFR — every admin-area string is
/// es-CR. Mirrors the <c>AdminCompaniesResources</c> static-constant pattern.
/// </summary>
public static class AdminReceptionWindowsResources
{
    public const string CardTitle = "Ventanas de recepción";
    public const string CardHelp =
        "Configure las fechas en que se aceptan solicitudes para este proceso. " +
        "Si no hay ninguna ventana, la recepción permanece abierta sin restricción.";

    // Form labels.
    public const string NameLabel = "Nombre de la ventana";
    public const string StartLabel = "Apertura (hora de Costa Rica)";
    public const string EndLabel = "Cierre (hora de Costa Rica)";
    public const string ApplicantMessageLabel = "Mensaje para la persona solicitante (opcional)";
    public const string DescriptionLabel = "Descripción interna (opcional)";
    public const string DisplayOrderLabel = "Orden";

    public const string AddButton = "Agregar ventana";
    public const string SaveButton = "Guardar";
    public const string EditButton = "Editar";
    public const string ActivateButton = "Activar";
    public const string DeactivateButton = "Desactivar";
    public const string DeleteButton = "Eliminar";

    // State badges.
    public const string BadgeUpcoming = "Próxima";
    public const string BadgeOpen = "Abierta";
    public const string BadgeClosed = "Cerrada";
    public const string BadgeInactive = "Inactiva";

    public const string NoWindows =
        "Este proceso no tiene ventanas de recepción. La recepción está abierta sin restricción.";

    // Confirmations (spec 024 dialog copy).
    public const string DeleteConfirm = "¿Eliminar esta ventana de recepción?";
    public const string DeactivateConfirm =
        "¿Desactivar esta ventana? Dejará de aceptar solicitudes y no se mostrará a las personas solicitantes.";

    // Toasts.
    public const string CreatedToast = "Ventana de recepción creada.";
    public const string UpdatedToast = "Ventana de recepción actualizada.";
    public const string ActivatedToast = "Ventana de recepción activada.";
    public const string DeactivatedToast = "Ventana de recepción desactivada.";
    public const string DeletedToast = "Ventana de recepción eliminada.";

    // Validation (es-CR) — the domain also rejects these as a backstop.
    public const string NameRequired = "El nombre de la ventana es obligatorio.";
    public const string EndAfterStart = "La fecha de cierre debe ser posterior a la fecha de apertura.";
    public const string DatesRequired = "Debe indicar la fecha de apertura y la de cierre.";
}
