using FundingPlatform.Application.Notifications;
using FundingPlatform.Application.Notifications.Templates;
using FundingPlatform.Domain.Notifications;
using FundingPlatform.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace FundingPlatform.Infrastructure.Notifications.Resolvers;

/// <summary>
/// Spec 021 / T030 / FR-006..FR-013 — recipient resolver. Reads applicant +
/// stage-group reviewers + participating admins at dispatch time so role
/// changes and email-address updates between event-fire and dispatch are
/// honoured (EC-002, EC-003, EC-004).
///
/// <para>Dedup order: applicant first; then reviewers by user id ascending;
/// then participating admins by user id ascending — deterministic ordering
/// so tests are reproducible.</para>
///
/// <para>Bucket priority on collision: <c>Applicant &gt; Reviewer &gt; Admin</c>.
/// The kept entry uses the bucket-priority-winning <see cref="NotificationRecipient.TemplateVariantKey"/>.</para>
/// </summary>
public sealed class NotificationRecipientResolver : INotificationRecipientResolver
{
    private readonly AppDbContext _context;
    private readonly UserManager<Domain.Entities.ApplicationUser> _userManager;
    private readonly ParticipatingAdminPredicate _adminPredicate;

    public NotificationRecipientResolver(
        AppDbContext context,
        UserManager<Domain.Entities.ApplicationUser> userManager,
        ParticipatingAdminPredicate adminPredicate)
    {
        _context = context;
        _userManager = userManager;
        _adminPredicate = adminPredicate;
    }

    public async Task<IReadOnlyList<NotificationRecipient>> ResolveAsync(
        NotificationOutboxResolveContext context,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(context);
        var binding = NotificationTemplateBindings.For(context.EventType);
        var applicantVariantKey = binding.TemplateVariantKey;
        // The reviewer-variant + admin-variant template keys reuse the binding's
        // TemplateVariantKey because per FR-024 participating admins reuse the
        // bucket-priority-winning body partial. The renderer dispatches on the
        // event type and recipient bucket — TemplateVariantKey is informational
        // for tests and observability.

        var candidates = new List<NotificationRecipient>();

        var includeApplicant = IncludesApplicantBucket(context.EventType);
        var includeReviewers = IncludesReviewerBucket(context.EventType);
        var includeAdmins    = IncludesAdminBucket(context.EventType);

        if (includeApplicant)
        {
            var applicantUser = await _context.Users
                .Where(u => u.Id == context.Payload.ApplicantUserId)
                .Select(u => new { u.Id, u.Email, u.UserName })
                .FirstOrDefaultAsync(ct);

            if (applicantUser is not null)
            {
                candidates.Add(new NotificationRecipient(
                    UserId: applicantUser.Id,
                    Email: applicantUser.Email ?? string.Empty,
                    DisplayName: context.Payload.ApplicantDisplayName,
                    Bucket: RecipientBucket.Applicant,
                    TemplateVariantKey: applicantVariantKey));
            }
        }

        if (includeReviewers)
        {
            var stageGroupIds = context.Payload.StageGroupIds;
            if (stageGroupIds.Count > 0)
            {
                var reviewerRows = await (
                    from m in _context.UserGroupMemberships
                    where stageGroupIds.Contains(m.GroupId)
                    join u in _context.Users on m.UserId equals u.Id
                    orderby u.Id
                    select new { u.Id, u.Email, u.UserName, u.FirstName, u.LastName })
                    .Distinct()
                    .ToListAsync(ct);

                foreach (var r in reviewerRows)
                {
                    candidates.Add(new NotificationRecipient(
                        UserId: r.Id,
                        Email: r.Email ?? string.Empty,
                        DisplayName: BuildDisplayName(r.FirstName, r.LastName, r.UserName),
                        Bucket: RecipientBucket.Reviewer,
                        TemplateVariantKey: applicantVariantKey));
                }
            }
        }

        if (includeAdmins)
        {
            var adminUserIds = await _adminPredicate.GetParticipatingAdminUserIdsAsync(
                context.ApplicationId, ct);

            if (adminUserIds.Count > 0)
            {
                var adminRows = await _context.Users
                    .Where(u => adminUserIds.Contains(u.Id))
                    .OrderBy(u => u.Id)
                    .Select(u => new { u.Id, u.Email, u.UserName, u.FirstName, u.LastName })
                    .ToListAsync(ct);

                foreach (var a in adminRows)
                {
                    candidates.Add(new NotificationRecipient(
                        UserId: a.Id,
                        Email: a.Email ?? string.Empty,
                        DisplayName: BuildDisplayName(a.FirstName, a.LastName, a.UserName),
                        Bucket: RecipientBucket.Admin,
                        TemplateVariantKey: applicantVariantKey));
                }
            }
        }

        // FR-012 — dedup by UserId (fallback to email when UserId is null) keeping
        // the lowest-ordinal bucket (Applicant < Reviewer < Admin).
        var deduped = candidates
            .GroupBy(c => c.UserId ?? c.Email)
            .Select(g => g.OrderBy(c => (int)c.Bucket).First())
            .ToList();

        return deduped;
    }

    private static bool IncludesApplicantBucket(NotificationEvent ev) => ev switch
    {
        NotificationEvent.ApplicationSubmittedApplicant => true,
        NotificationEvent.ReturnedToApplicant           => true,
        NotificationEvent.ApplicationApproved           => true,
        NotificationEvent.ApplicationRejected           => true,
        _ => false,
    };

    private static bool IncludesReviewerBucket(NotificationEvent ev) => ev switch
    {
        NotificationEvent.ApplicationSubmittedReviewer => true,
        NotificationEvent.ResubmittedByApplicant       => true,
        _ => false,
    };

    private static bool IncludesAdminBucket(NotificationEvent ev) => ev switch
    {
        NotificationEvent.ApplicationSubmittedApplicant => false,  // applicant-only
        _ => true,
    };

    private static string BuildDisplayName(string? firstName, string? lastName, string? userName)
    {
        var full = $"{firstName} {lastName}".Trim();
        if (full.Length > 0) return full;
        return userName ?? string.Empty;
    }
}
