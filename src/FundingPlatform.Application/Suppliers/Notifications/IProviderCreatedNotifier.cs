namespace FundingPlatform.Application.Suppliers.Notifications;

/// <summary>
/// Spec 038 (US4) — emails every Auditor when a provider is created. Best-effort:
/// implementations MUST NOT throw to the caller (catch + log internally) so a
/// failed notification never blocks provider creation (FR-024). Sends through the
/// allowlist-wrapped Notifications <c>IEmailSender</c> (not the direct-send path).
/// </summary>
public interface IProviderCreatedNotifier
{
    Task NotifyAuditorsAsync(int supplierId, CancellationToken ct);
}
