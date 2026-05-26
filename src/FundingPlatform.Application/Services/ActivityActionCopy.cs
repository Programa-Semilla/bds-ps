namespace FundingPlatform.Application.Services;

/// <summary>
/// es-CR (default culture) display copy for VersionHistory action codes, shared
/// by every dashboard "recent activity" timeline (applicant + reviewer). The raw
/// action codes are internal English identifiers; this is the single place that
/// turns them into user-facing Spanish — keeping the applicant and reviewer
/// surfaces from drifting apart (cf. <see cref="ApplicationCurrencyTotal"/>).
/// </summary>
public static class ActivityActionCopy
{
    public static string Title(string action) => action switch
    {
        "Created"               => "Solicitud creada",
        "Submitted"             => "Solicitud enviada",
        "StartReview"           => "Revisión iniciada",
        "ReviewItem"            => "Ítem revisado",
        "SendBack"              => "Devuelta para más detalles",
        "FlagEquivalence"       => "Equivalencia técnica actualizada",
        "Withdrawn"             => "Solicitud retirada",
        "Finalize"              => "Decisión registrada",
        "AgreementGenerated"    => "Convenio generado",
        "AgreementRegenerated"  => "Convenio regenerado",
        "AgreementExecuted"     => "Convenio firmado",
        "Funded"                => "Fondos entregados",
        _                       => action,
    };
}
