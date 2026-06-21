using System.Text.Json;

namespace FundingPlatform.Application.Notifications;

/// <summary>
/// Spec 021 / T013 / FR-002 — serialized payload stashed in
/// <c>NotificationOutbox.PayloadJson</c>. Captures the data the resolver +
/// renderer need to identify the application + applicant + stage group at
/// outbox-write time. Recipient identity is NOT snapshotted here — the
/// resolver re-reads at dispatch time per EC-003 / EC-004.
/// </summary>
/// <param name="ApplicationId">Application aggregate id.</param>
/// <param name="ApplicantUserId">Applicant's ASP.NET Identity user id; used for the applicant bucket.</param>
/// <param name="ApplicantDisplayName">First + Last, used in subjects + bodies; safe to render.</param>
/// <param name="StageGroupIds">Group ids assigned to the current workflow stage (drives reviewer bucket).</param>
/// <param name="OutcomeCode">
/// "Approved" / "Rejected" for terminal events; for spec-028 <c>AppealResolvedApplicant</c>
/// carries "AppealUpheld" / "AppealReopenedToDraft" / "AppealReopenedToReview" (R-004);
/// null otherwise.
/// </param>
/// <param name="ActorUserId">
/// Spec 028 / R-003 / FR-013a — the user who triggered the event. The resolver drops this
/// id from the final recipient set (actor exclusion) so an actor who is also a participating
/// admin never receives a copy of their own action. Optional + nullable: legacy spec-021 rows
/// have no such field and deserialize to null, which the resolver treats as "no actor to exclude".
/// </param>
/// <param name="AuditFindings">
/// Spec 040 / FR-011 — for <c>ReturnedToReviewerFromAudit</c>, the auditor's per-item
/// non-compliance findings ("item — reason") rendered in the email body so the reviewer
/// sees the reasons without opening the app. Optional + nullable: every other event
/// serializes/deserializes it as null.
/// </param>
public sealed record NotificationPayload(
    int ApplicationId,
    string ApplicantUserId,
    string ApplicantDisplayName,
    IReadOnlyList<int> StageGroupIds,
    string? OutcomeCode,
    string? ActorUserId = null,
    IReadOnlyList<string>? AuditFindings = null)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        // No camelCase normalization: keep the wire format identical to the
        // C# record property names so operators reading raw rows can map
        // tokens to code without a translation step.
        PropertyNamingPolicy = null,
        // es-CR data carries accented characters (Pérez, Cárdenas, etc.). The
        // default Unicode escaping makes raw rows hard to read for operators;
        // the relaxed encoder writes UTF-8 bytes directly and the dacpac column
        // (NVARCHAR(MAX)) handles them natively.
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>
    /// Serializes to a stable JSON shape under 4 KB (logical cap per data-model.md).
    /// </summary>
    public string Serialize() => JsonSerializer.Serialize(this, JsonOptions);

    /// <summary>
    /// Deserializes a payload row. Throws <see cref="JsonException"/> on malformed
    /// input; the worker maps that to PermanentFailure (render exception path).
    /// </summary>
    public static NotificationPayload Deserialize(string json)
        => JsonSerializer.Deserialize<NotificationPayload>(json, JsonOptions)
           ?? throw new JsonException("NotificationPayload payload was null after deserialization");
}
