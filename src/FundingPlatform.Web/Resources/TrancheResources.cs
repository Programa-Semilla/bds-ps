using FundingPlatform.Application.Disbursements;

namespace FundingPlatform.Web.Resources;

/// <summary>
/// Spec 046 — es-CR copy for the tranche (Tramos) editor on the reviewer review surface
/// and the budget-line composed balance/filter surface on the disbursement page. Service-
/// produced refusal strings live in <c>DisbursementReasons</c> (Application); this holds the
/// view/controller copy (titles, labels, buttons, status labels). All es-CR — no English
/// literals in views (Constitution / conventions).
/// </summary>
public static class TrancheResources
{
    // Tranche editor card (reviewer pre-audit surface)
    public const string Editor_Title = "Tramos del presupuesto";
    public const string Editor_Subtitle =
        "Agrupe las líneas del presupuesto en tramos (fases de financiamiento). El monto de cada tramo se deriva de la suma de sus líneas.";
    public const string Editor_FrozenNotice =
        "La estructura de tramos quedó congelada al ejecutarse el convenio y ya no puede modificarse.";

    // Synthetic default tranche (no DB row) — shown when ≥1 line is unassigned. The canonical
    // value lives in the Application layer so the projection (Infrastructure) can name it too.
    public const string SyntheticTrancheName = ComposedBalanceDefaults.SyntheticTrancheName;

    // Create / rename / delete
    public const string Field_TrancheName = "Nombre del tramo";
    public const string Action_CreateTranche = "Crear tramo";
    public const string Action_RenameTranche = "Renombrar";
    public const string Action_DeleteTranche = "Eliminar tramo";
    public const string Confirm_DeleteTrancheTitle = "Eliminar tramo";
    public const string Confirm_DeleteTrancheBody =
        "¿Confirma la eliminación de este tramo? Sus líneas volverán al tramo «General».";
    public const string Confirm_DeleteTrancheLabel = "Eliminar tramo";

    // Line ↔ tranche assignment
    public const string Assign_Heading = "Asignar líneas a tramos";
    public const string Assign_Line = "Línea";
    public const string Assign_Budget = "Presupuesto";
    public const string Assign_Tranche = "Tramo";
    public const string Assign_Unassigned = "(sin asignar → General)";
    public const string Action_Assign = "Asignar";

    // Derived amounts
    public const string DerivedAmount = "Monto derivado";
    public const string TrancheAllocationTotal = "Total asignado (todos los tramos)";

    // Flashes
    public const string Flash_TrancheCreated = "Tramo creado.";
    public const string Flash_TrancheRenamed = "Tramo renombrado.";
    public const string Flash_TrancheDeleted = "Tramo eliminado.";
    public const string Flash_LineAssigned = "Línea asignada al tramo.";

    // Budget-line composed panel (disbursement surface)
    public const string Panel_Title = "Ejecución por tramo y línea";
    public const string Panel_Col_Line = "Línea";
    public const string Panel_Col_Supplier = "Proveedor";
    public const string Panel_Col_Allocated = "Asignado";
    public const string Panel_Col_Committed = "Comprometido";
    public const string Panel_Col_Paid = "Pagado";
    public const string Panel_Col_Available = "Disponible";
    public const string Panel_Col_Status = "Estado";
    public const string Panel_Empty = "Esta solicitud no tiene líneas presupuestarias.";

    // Budget-line filter toolbar (US4)
    public const string Filter_Tranche = "Tramo";
    public const string Filter_Status = "Estado";
    public const string Filter_Supplier = "Proveedor";
    public const string Filter_ValidationState = "Validación";
    public const string Filter_All = "Todos";
    public const string Filter_Apply = "Filtrar";
    public const string Filter_Clear = "Limpiar";
    public const string ValidationState_HasPending = "Con pendientes";
    public const string ValidationState_FullyValidated = "Totalmente validada";

    /// <summary>Spec 046 / D3 — es-CR label for the derived budget-line status.</summary>
    public static string StatusLabel(BudgetLineStatus status) => status switch
    {
        BudgetLineStatus.Uncommitted => "Sin comprometer",
        BudgetLineStatus.Committed => "Comprometida",
        BudgetLineStatus.PartiallyPaid => "Pago parcial",
        BudgetLineStatus.Paid => "Pagada",
        BudgetLineStatus.Validated => "Validada",
        _ => status.ToString(),
    };

    /// <summary>Tabler badge colour for the status pill (never the sole signal — the label accompanies it).</summary>
    public static string StatusBadgeClass(BudgetLineStatus status) => status switch
    {
        BudgetLineStatus.Uncommitted => "bg-secondary-lt",
        BudgetLineStatus.Committed => "bg-blue-lt",
        BudgetLineStatus.PartiallyPaid => "bg-yellow-lt",
        BudgetLineStatus.Paid => "bg-azure-lt",
        BudgetLineStatus.Validated => "bg-green-lt",
        _ => "bg-secondary-lt",
    };
}
