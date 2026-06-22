namespace FundingPlatform.Web.Resources;

/// <summary>
/// Spec 044 / US2+US3 — es-CR copy for the applicant-facing reception-window
/// surfaces: the submission-refusal message (mapped to HTTP 422 via the
/// <c>DomainExceptionFilter</c>) and the create/edit timing notice. Instant
/// strings are pre-formatted in Costa Rica local time (<c>dd/MM/yyyy HH:mm</c>)
/// by the caller via <c>IBusinessTimeZone</c>; these methods only compose copy.
/// </summary>
public static class ReceptionWindowResources
{
    // ----- Submission-refusal (FR-008) -------------------------------------------
    /// <summary>Refusal when the first/next window has not opened yet (BeforeFirst/Between).</summary>
    public static string RefusalBeforeOpen(string openInstant)
        => $"La recepción de solicitudes abre el {openInstant} (hora de Costa Rica).";

    /// <summary>Refusal when every window has already closed (AllWindowsClosed).</summary>
    public static string RefusalAllClosed(string closedInstant)
        => $"La recepción de solicitudes ya cerró el {closedInstant} (hora de Costa Rica).";

    /// <summary>Generic refusal when no boundary instant is available.</summary>
    public const string RefusalGeneric = "La recepción de solicitudes no está abierta en este momento.";

    public const string RefusalTitle = "Recepción cerrada";

    // ----- Applicant notice (US3) -------------------------------------------------
    public const string OpenHeading = "Recepción abierta";
    public static string OpenBody(string closeInstant)
        => $"Puede enviar su solicitud hasta el {closeInstant} (hora de Costa Rica).";
    public static string OpenCountdown(string remaining)
        => $"Tiempo restante: {remaining}.";

    public const string UpcomingHeading = "Recepción próxima";
    public static string UpcomingBody(string openInstant)
        => $"La recepción abre el {openInstant} (hora de Costa Rica). Puede preparar un borrador mientras tanto.";

    public const string ClosedHeading = "Recepción cerrada";
    public static string ClosedBody(string closedInstant)
        => $"La recepción cerró el {closedInstant} (hora de Costa Rica). Ya no se aceptan nuevas solicitudes.";

    /// <summary>Shown next to a disabled submit control when not open.</summary>
    public const string SubmitDisabledOpen = "El envío está disponible solo durante una ventana de recepción abierta.";
}
