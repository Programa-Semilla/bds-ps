using System.Globalization;
using FundingPlatform.Domain.Enums;

namespace FundingPlatform.Web.Resources;

/// <summary>
/// Spec 043 — es-CR user-facing copy for the regulatory-freshness gate, the
/// non-blocking warning, the Hacienda sync-failure surface, and the stale-value
/// digest. Mirrors the static-class resource pattern (e.g.
/// <c>FundsUsageEvidenceResources</c>); never stored in the DB.
/// </summary>
public static class RegulatoryFreshnessResources
{
    // ----- Hard gate (US1, FR-007) -----
    public const string Block_Heading =
        "No se puede avanzar: uno o más proveedores tienen información regulatoria vencida.";

    // ----- Non-blocking warning (US4, FR-010) -----
    public const string Warning_Heading =
        "Atención: uno o más proveedores tienen información regulatoria vencida o sin revisar.";

    // ----- Sync-failure surface (US3) -----
    public const string SyncFailure_Label = "verificación fallida";
    public const string SyncFailure_FilterLabel = "Verificación fallida";
    public const string SyncSuccess_Label = "Verificación exitosa";
    public const string SyncNeverAttempted_Label = "sin verificar";

    // ----- Digest (US4) -----
    public const string Digest_Subject = "Proveedores con información regulatoria vencida";
    public const string Digest_HeroTitle = "Proveedores con información regulatoria vencida";
    public const string Digest_Intro =
        "Las siguientes solicitudes en auditoría tienen proveedores cuya información regulatoria está vencida o sin revisar. Actualizá la información del proveedor para poder avanzar.";
    public const string Digest_CardHeading = "Solicitudes afectadas";
    public const string Digest_NeverReviewed = "sin revisar";

    /// <summary>es-CR label for a regulatory field (FR-007 — name the field).</summary>
    public static string FieldLabel(RegulatoryField field) => field switch
    {
        RegulatoryField.Hacienda => "Hacienda",
        RegulatoryField.Ccss => "CCSS / Caja",
        RegulatoryField.Sicop => "SICOP",
        _ => field.ToString(),
    };

    /// <summary>es-CR label for a sync outcome.</summary>
    public static string OutcomeLabel(HaciendaSyncOutcome? outcome) => outcome switch
    {
        HaciendaSyncOutcome.Success => SyncSuccess_Label,
        HaciendaSyncOutcome.Failure => SyncFailure_Label,
        _ => SyncNeverAttempted_Label,
    };

    /// <summary>
    /// One enumerated finding line: "Proveedor X — Hacienda — revisado por última vez
    /// el dd/MM/yyyy" (or "sin revisar"). Used by both the block message and the warning.
    /// </summary>
    public static string FindingLine(string supplierName, RegulatoryField field, DateTime? lastReviewedAt)
    {
        var when = lastReviewedAt is { } at
            ? $"revisado por última vez el {at.ToString("dd/MM/yyyy", new CultureInfo("es-CR"))}"
            : "sin revisar";
        return $"{supplierName} — {FieldLabel(field)} — {when}";
    }
}
