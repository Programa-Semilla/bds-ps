using FundingPlatform.Domain.Entities;

namespace FundingPlatform.Application.Services;

/// <summary>
/// Spec 017 / US7 / R5 — es-CR mapping of the shipped
/// <see cref="AdminAuditEvent"/> actions (group, user, and spec-038 supplier
/// regulatory actions), with a generic fallback. Voice-guide compliant: third-person
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
            // Spec 038 — provider regulatory compliance (auditor) events.
            AdminAuditEvent.SupplierRegulatoryChanged => "actualizó el cumplimiento regulatorio del proveedor",
            AdminAuditEvent.SupplierRegulatoryReviewed => "confirmó la revisión regulatoria del proveedor",
            AdminAuditEvent.SupplierPmeChanged => "actualizó la condición PYME del proveedor",
            AdminAuditEvent.SupplierWarningChanged => "actualizó la advertencia del proveedor",
            _ => "registró un cambio en",
        };
    }
}
