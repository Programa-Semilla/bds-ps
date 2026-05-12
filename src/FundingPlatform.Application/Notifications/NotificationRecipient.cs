namespace FundingPlatform.Application.Notifications;

/// <summary>
/// Spec 021 / T012 / FR-006 — resolver output value object. Not persisted.
/// One instance per <c>(UserId, OutboxRow)</c> pair after dedup.
///
/// <para>
/// <c>UserId</c> is nullable to leave room for future synthetic recipients
/// (none ship in v1). When <c>UserId</c> is null the worker dedupes on
/// <c>Email</c>.
/// </para>
/// <para>
/// <c>Email</c> may be empty when the user has no email on file; the worker
/// then writes a Skipped delivery row with LastError="MissingEmail" (FR-029).
/// </para>
/// <para>
/// <c>TemplateVariantKey</c> is the key into
/// <see cref="Templates.NotificationTemplateBindings"/> so the renderer
/// picks the right body partial without re-computing event variants.
/// </para>
/// </summary>
public sealed record NotificationRecipient(
    string? UserId,
    string Email,
    string DisplayName,
    RecipientBucket Bucket,
    string TemplateVariantKey);
