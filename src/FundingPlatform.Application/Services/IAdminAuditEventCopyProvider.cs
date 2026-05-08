namespace FundingPlatform.Application.Services;

/// <summary>
/// Spec 017 / US7 / R5 — maps an <c>AdminAuditEvent</c> action + target type to
/// voice-guide-compliant es-CR copy. Single seam for future action-vocabulary
/// additions; the four shipped mappings cover the spec 016 action set:
/// <c>group.create</c>, <c>group.rename</c>, <c>group.delete</c>,
/// <c>user.memberships.update</c>.
/// </summary>
public interface IAdminAuditEventCopyProvider
{
    /// <summary>
    /// Format the third-person past-tense phrase that appears between the actor
    /// and the deep-link to the target. The provider does not own the target
    /// label; the projection appends it (e.g. group name) when available.
    /// </summary>
    string Format(string action, string targetType, string? payloadJson);
}
