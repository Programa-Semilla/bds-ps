using System.Globalization;
using FundingPlatform.Domain.Enums;

namespace FundingPlatform.Application.Regulatory;

/// <summary>
/// Spec 043 — es-CR copy + formatters shared across layers for the freshness gate
/// (US1), the non-blocking warning (US4), and the stale-value digest (US4). Lives
/// in Application (not Web.Resources) because Infrastructure
/// (<c>AuditWorkflowService</c>, the digest factory) also produces these strings —
/// the same Clean-Architecture exception spec 034 made for <c>BatchUserRowReasons</c>.
/// The enumerated finding lines are data + es-CR presentation, not domain English.
/// </summary>
public static class RegulatoryFreshnessCopy
{
    private static readonly CultureInfo EsCr = new("es-CR");

    public const string BlockHeading =
        "No se puede avanzar: uno o más proveedores tienen información regulatoria vencida o sin revisar.";

    public const string WarningHeading =
        "Atención: uno o más proveedores tienen información regulatoria vencida o sin revisar.";

    // Digest copy (used by the Infrastructure digest factory).
    public const string DigestSubject = "Proveedores con información regulatoria vencida";
    public const string DigestHeroTitle = "Proveedores con información regulatoria vencida";
    public const string DigestIntro =
        "Las siguientes solicitudes en auditoría tienen proveedores cuya información regulatoria está vencida o sin revisar. Actualizá la información del proveedor para poder avanzar.";
    public const string DigestCardHeading = "Solicitudes afectadas";

    /// <summary>es-CR label for a regulatory field (FR-007 — name the field).</summary>
    public static string FieldLabel(RegulatoryField field) => field switch
    {
        RegulatoryField.Hacienda => "Hacienda",
        RegulatoryField.Ccss => "CCSS / Caja",
        RegulatoryField.Sicop => "SICOP",
        _ => field.ToString(),
    };

    /// <summary>One enumerated finding line: "Proveedor X — Hacienda — sin revisar" or
    /// "… — revisado por última vez el dd/MM/yyyy" (FR-007 names provider+field+last-reviewed).</summary>
    public static string FindingLine(string supplierName, RegulatoryField field, DateTime? lastReviewedAt)
    {
        var when = lastReviewedAt is { } at
            ? $"revisado por última vez el {at.ToString("dd/MM/yyyy", EsCr)}"
            : "sin revisar";
        return $"{supplierName} — {FieldLabel(field)} — {when}";
    }

    /// <summary>FR-007 — the full es-CR block message: heading + enumerated findings.</summary>
    public static string BuildBlockMessage(IReadOnlyList<StaleRegulatoryFinding> findings)
        => $"{BlockHeading} {Enumerate(findings)}";

    /// <summary>FR-010 — the full es-CR warning message: heading + enumerated findings.</summary>
    public static string BuildWarningMessage(IReadOnlyList<StaleRegulatoryFinding> findings)
        => $"{WarningHeading} {Enumerate(findings)}";

    private static string Enumerate(IReadOnlyList<StaleRegulatoryFinding> findings)
        => string.Join("; ", findings.Select(f => FindingLine(f.SupplierName, f.Field, f.LastReviewedAt)));
}
