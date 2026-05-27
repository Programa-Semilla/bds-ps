using FundingPlatform.Domain.Enums;

namespace FundingPlatform.Application.Services;

/// <summary>
/// es-CR (default culture) display copy for <see cref="ItemResponseDecision"/> —
/// the applicant's per-item accept/reject decision. The enum members are internal
/// English identifiers; this is the single place that turns them into user-facing
/// Spanish so the read-only "Su decisión" column never leaks "Accept"/"Reject"
/// (cf. <see cref="ActivityActionCopy"/>). Feminine forms agree with "decisión"/"respuesta".
/// </summary>
public static class ItemResponseDecisionCopy
{
    public static string Label(ItemResponseDecision decision) => decision switch
    {
        ItemResponseDecision.Accept => "Aceptada",
        ItemResponseDecision.Reject => "Rechazada",
        _                           => decision.ToString(),
    };
}
