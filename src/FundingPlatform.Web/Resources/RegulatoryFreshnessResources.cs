using FundingPlatform.Application.Regulatory;
using FundingPlatform.Domain.Enums;

namespace FundingPlatform.Web.Resources;

/// <summary>
/// Spec 043 — Web-view es-CR copy for the regulatory-freshness surfaces (warning
/// partial, supplier sync-outcome display, admin-list filter). The cross-layer
/// gate/warning/digest formatters live in
/// <see cref="FundingPlatform.Application.Regulatory.RegulatoryFreshnessCopy"/>;
/// this class delegates field labels there so the wording never diverges.
/// </summary>
public static class RegulatoryFreshnessResources
{
    public const string Warning_Heading = RegulatoryFreshnessCopy.WarningHeading;

    // ----- Sync-outcome surface (US3) -----
    public const string SyncFailure_Label = "verificación fallida";
    public const string SyncFailure_FilterLabel = "Verificación fallida";
    public const string SyncSuccess_Label = "verificación exitosa";
    public const string SyncNeverAttempted_Label = "sin verificar";

    /// <summary>es-CR label for a regulatory field (delegates to the shared copy).</summary>
    public static string FieldLabel(RegulatoryField field) => RegulatoryFreshnessCopy.FieldLabel(field);

    /// <summary>es-CR label for a sync outcome.</summary>
    public static string OutcomeLabel(HaciendaSyncOutcome? outcome) => outcome switch
    {
        HaciendaSyncOutcome.Success => SyncSuccess_Label,
        HaciendaSyncOutcome.Failure => SyncFailure_Label,
        _ => SyncNeverAttempted_Label,
    };
}
