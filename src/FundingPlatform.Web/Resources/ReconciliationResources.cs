namespace FundingPlatform.Web.Resources;

/// <summary>
/// Spec 048 / US3 — es-CR copy for the reconciliation dashboard (Reconciliación) surface. Refusal
/// strings produced by the service live in
/// <c>FundingPlatform.Application.Reconciliation.DiscrepancyReasons</c>; this holds the view/controller
/// copy (titles, tiles, filter labels, timeline labels, buttons). All es-CR — no English in views.
/// </summary>
public static class ReconciliationResources
{
    // Page + nav
    public const string Nav = "Reconciliación";
    public const string Title = "Reconciliación";
    public const string Subtitle = "Diferencias detectadas en la ejecución financiera, por severidad y estado.";

    // Summary tiles
    public const string Tile_OpenBlocking = "Bloqueantes abiertas";
    public const string Tile_OpenBlockingAmount = "Monto bloqueante";
    public const string Tile_OpenWarning = "Advertencias abiertas";
    public const string Tile_OpenWarningAmount = "Monto en advertencias";
    public const string Rollup_Heading = "Por fondo";
    public const string Rollup_Fund = "Fondo";
    public const string Rollup_Blocking = "Bloqueantes";
    public const string Rollup_Warning = "Advertencias";

    // Filter toolbar
    public const string Filter_Heading = "Filtros";
    public const string Filter_Severity = "Severidad";
    public const string Filter_State = "Estado";
    public const string Filter_Supplier = "Proveedor";
    public const string Filter_Tranche = "Tramo";
    public const string Filter_Participant = "Participante (APP-#####)";
    public const string Filter_Responsible = "Responsable";
    public const string Filter_DateFrom = "Desde";
    public const string Filter_DateTo = "Hasta";
    public const string Filter_Apply = "Aplicar";
    public const string Filter_Clear = "Limpiar";
    public const string Filter_All = "Todas";
    public const string Filter_OpenOnly = "Solo abiertas";

    // List
    public const string Col_Participant = "Participante";
    public const string Col_Scope = "Ámbito";
    public const string Col_Comparison = "Comparación";
    public const string Col_Severity = "Severidad";
    public const string Col_State = "Estado";
    public const string Col_Difference = "Diferencia";
    public const string Col_Responsible = "Responsable";
    public const string Col_Detected = "Detectada";
    public const string Col_Actions = "";
    public const string Empty = "No hay diferencias que coincidan con los filtros.";
    public const string Action_Open = "Ver detalle";
    public const string Unassigned = "Sin asignar";

    // Detail
    public const string Detail_Title = "Detalle de la diferencia";
    public const string Detail_Expected = "Monto esperado";
    public const string Detail_Actual = "Monto registrado";
    public const string Detail_Difference = "Diferencia";
    public const string Detail_Source = "Documento de origen";
    public const string Detail_Participant = "Participante";
    public const string Detail_Line = "Línea presupuestaria";
    public const string Detail_Tranche = "Tramo";
    public const string Detail_Supplier = "Proveedor";
    public const string Detail_RequiredAction = "Acción requerida";
    public const string Detail_Timeline = "Historial de corrección";
    public const string Detail_Back = "Volver al panel";
    public const string Timeline_By = "por";

    // Write actions (Financial Operator only)
    public const string Action_Assign = "Asignar";
    public const string Action_UnderCorrection = "Marcar en corrección";
    public const string Action_Waive = "Exonerar";
    public const string Field_Assignee = "Responsable";
    public const string Field_Note = "Nota (opcional)";
    public const string Field_WaiveReason = "Motivo de la exoneración";
    public const string ReadOnly_Notice = "Vista de solo lectura.";

    // Flashes
    public const string Flash_Assigned = "Diferencia asignada.";
    public const string Flash_UnderCorrection = "Diferencia marcada en corrección.";
    public const string Flash_Waived = "Diferencia exonerada.";
}
