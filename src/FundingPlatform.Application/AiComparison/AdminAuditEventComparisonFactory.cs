using System.Text.Json;
using FundingPlatform.Application.Abstractions.AiComparison;
using FundingPlatform.Domain.Entities;

namespace FundingPlatform.Application.AiComparison;

/// <summary>
/// Spec 020 / FR-H1..FR-H3 — assembles <see cref="AdminAuditEvent"/> rows for
/// successful + failed comparison generations. Payload shape mirrors
/// contracts/audit-event-payload.md verbatim.
/// </summary>
public class AdminAuditEventComparisonFactory
{
    public const string ActionGenerated = "AiComparisonGenerated";
    public const string ActionFailed = "AiComparisonFailed";
    public const string TargetType = "ApplicationItem";

    public AdminAuditEvent BuildSuccess(SuccessAuditPayload payload)
    {
        var json = JsonSerializer.Serialize(BuildBaseDict(payload, success: true));
        return AdminAuditEvent.Record(
            actorUserId: payload.ActorUserId,
            action: ActionGenerated,
            targetType: TargetType,
            targetId: payload.ApplicationItemId.ToString(),
            payloadJson: json);
    }

    public AdminAuditEvent BuildFailure(FailureAuditPayload payload)
    {
        var dict = BuildBaseDict(payload, success: false);
        dict["failureReason"] = payload.FailureReason;
        var json = JsonSerializer.Serialize(dict);
        return AdminAuditEvent.Record(
            actorUserId: payload.ActorUserId,
            action: ActionFailed,
            targetType: TargetType,
            targetId: payload.ApplicationItemId.ToString(),
            payloadJson: json);
    }

    private static Dictionary<string, object?> BuildBaseDict(BaseAuditPayload payload, bool success) => new()
    {
        ["v"] = 1,
        ["applicationId"] = payload.ApplicationId,
        ["applicationItemId"] = payload.ApplicationItemId,
        ["actorUserId"] = payload.ActorUserId,
        ["actorRole"] = payload.ActorRole,
        ["supplierIds"] = payload.SupplierIds,
        ["inputHash"] = payload.InputHash,
        ["promptVersion"] = payload.PromptVersion,
        ["schemaVersion"] = payload.SchemaVersion,
        ["aiModel"] = payload.AiModel,
        ["extractModel"] = payload.ExtractModel,
        ["tokenCostInput"] = payload.TokenCostInput,
        ["tokenCostOutput"] = payload.TokenCostOutput,
        ["latencyMs"] = payload.LatencyMs,
        ["success"] = success,
        ["bypassedRateLimit"] = payload.BypassedRateLimit,
        ["bypassedTokenCap"] = payload.BypassedTokenCap,
        ["redactedFieldCounts"] = payload.RedactedFieldCounts,
    };
}

public abstract record BaseAuditPayload(
    int ApplicationId,
    int ApplicationItemId,
    string ActorUserId,
    string ActorRole,
    IReadOnlyList<int> SupplierIds,
    string InputHash,
    string PromptVersion,
    string SchemaVersion,
    string AiModel,
    string ExtractModel,
    int TokenCostInput,
    int TokenCostOutput,
    int LatencyMs,
    bool BypassedRateLimit,
    bool BypassedTokenCap,
    IReadOnlyDictionary<string, int> RedactedFieldCounts);

public sealed record SuccessAuditPayload(
    int ApplicationId,
    int ApplicationItemId,
    string ActorUserId,
    string ActorRole,
    IReadOnlyList<int> SupplierIds,
    string InputHash,
    string PromptVersion,
    string SchemaVersion,
    string AiModel,
    string ExtractModel,
    int TokenCostInput,
    int TokenCostOutput,
    int LatencyMs,
    bool BypassedRateLimit,
    bool BypassedTokenCap,
    IReadOnlyDictionary<string, int> RedactedFieldCounts)
    : BaseAuditPayload(ApplicationId, ApplicationItemId, ActorUserId, ActorRole,
        SupplierIds, InputHash, PromptVersion, SchemaVersion, AiModel, ExtractModel,
        TokenCostInput, TokenCostOutput, LatencyMs, BypassedRateLimit, BypassedTokenCap,
        RedactedFieldCounts);

public sealed record FailureAuditPayload(
    int ApplicationId,
    int ApplicationItemId,
    string ActorUserId,
    string ActorRole,
    IReadOnlyList<int> SupplierIds,
    string InputHash,
    string PromptVersion,
    string SchemaVersion,
    string AiModel,
    string ExtractModel,
    int TokenCostInput,
    int TokenCostOutput,
    int LatencyMs,
    bool BypassedRateLimit,
    bool BypassedTokenCap,
    IReadOnlyDictionary<string, int> RedactedFieldCounts,
    string FailureReason)
    : BaseAuditPayload(ApplicationId, ApplicationItemId, ActorUserId, ActorRole,
        SupplierIds, InputHash, PromptVersion, SchemaVersion, AiModel, ExtractModel,
        TokenCostInput, TokenCostOutput, LatencyMs, BypassedRateLimit, BypassedTokenCap,
        RedactedFieldCounts);
