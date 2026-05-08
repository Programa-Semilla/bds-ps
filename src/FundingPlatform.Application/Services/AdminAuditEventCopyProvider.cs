using FundingPlatform.Domain.Entities;

namespace FundingPlatform.Application.Services;

/// <summary>
/// Spec 017 / US7 / R5 — es-CR mapping of the four shipped
/// <see cref="AdminAuditEvent"/> actions. Voice-guide compliant: third-person
/// past tense, no exclamation marks, no "submit" CTAs. The projection appends
/// the target name (when it can be resolved) after this phrase.
/// </summary>
public sealed class AdminAuditEventCopyProvider : IAdminAuditEventCopyProvider
{
    public string Format(string action, string targetType, string? payloadJson)
    {
        return action switch
        {
            AdminAuditEvent.ActionGroupCreate => "creó el grupo",
            AdminAuditEvent.ActionGroupRename => "renombró el grupo",
            AdminAuditEvent.ActionGroupDelete => "eliminó el grupo",
            AdminAuditEvent.ActionUserMembershipsUpdate => "actualizó las membresías de",
            _ => "registró un cambio en",
        };
    }
}
