namespace FundingPlatform.Web.ViewModels.Admin;

/// <summary>
/// Spec 033 / FR-008 / C5 — backs the "Invitación enviada" confirmation rendered
/// directly from the create/resend POST. Carries the recipient email and the raw
/// invite link so the admin can copy it as a delivery-resilience fallback. The
/// raw link is shown ONCE (only its token hash is persisted); navigating away
/// requires a resend to obtain a fresh link.
/// </summary>
public sealed record AdminUserInvitationSentViewModel(string Email, string InviteLink);
