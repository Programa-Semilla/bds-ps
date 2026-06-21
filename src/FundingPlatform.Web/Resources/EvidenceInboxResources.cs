namespace FundingPlatform.Web.Resources;

/// <summary>
/// Spec 041 — es-CR copy for the funds-usage evidence inbox (the persistent
/// sidebar entry that lists executed applications in active processes). FR-009:
/// every label, the page title, and the empty state are es-CR (no English
/// literals in views). The per-application evidence page reuses
/// <see cref="FundsUsageEvidenceResources"/>.
/// </summary>
public static class EvidenceInboxResources
{
    // Sidebar entry + page heading
    public const string Nav = "Evidencia de uso de fondos";
    public const string Title = "Evidencia de uso de fondos";
    public const string Subtitle = "Postulaciones con convenio ejecutado en procesos activos. Abra una para registrar la evidencia del uso de los fondos.";

    // List columns
    public const string Col_Number = "Número";
    public const string Col_Applicant = "Solicitante";
    public const string Col_Fund = "Fondo";
    public const string Col_Process = "Proceso";
    public const string Col_Executed = "Ejecutado";
    public const string Action_Open = "Abrir";

    // Empty state
    public const string Empty = "No hay solicitudes con convenio ejecutado en procesos activos.";
}
