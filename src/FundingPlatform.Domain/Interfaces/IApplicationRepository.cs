using FundingPlatform.Domain.Entities;
using FundingPlatform.Domain.Enums;

namespace FundingPlatform.Domain.Interfaces;

/// <summary>
/// Spec 016 — group-overlap scope hint used by reviewer-facing repository
/// queries. Lives in Domain so the repository signature does not pull a
/// reference on the Application layer; the controller layer constructs the
/// hint from <c>IReviewerScope</c>.
/// </summary>
public sealed record ReviewerScopeHint(bool IsAdmin, IReadOnlyCollection<int> GroupIds)
{
    /// <summary>Admin / unscoped — every reviewer-facing query short-circuits.</summary>
    public static readonly ReviewerScopeHint Admin = new(true, Array.Empty<int>());
}

public interface IApplicationRepository
{
    Task<Application?> GetByIdAsync(int id);
    Task<Application?> GetByIdWithDetailsAsync(int id);

    /// <summary>Spec 020 — resolves the parent ApplicationId for a given Item id. Returns null when unknown.</summary>
    Task<int?> GetApplicationIdForItemAsync(int applicationItemId, CancellationToken ct);

    Task<Application?> GetByIdWithResponseAndAppealsAsync(int id);
    Task<List<Application>> GetByApplicantIdAsync(int applicantId);
    Task<List<Application>> GetForApplicantDashboardAsync(int applicantId);
    Task<(List<Application> Items, int TotalCount)> GetByStatePagedAsync(ApplicationState state, int page, int pageSize);

    /// <summary>
    /// Spec 016 / NFR-001 — same as <see cref="GetByStatePagedAsync(ApplicationState, int, int)"/>
    /// but applies the group-overlap predicate at the EF query level when the
    /// scope is not admin. Used by the reviewer queue projection.
    /// </summary>
    Task<(List<Application> Items, int TotalCount)> GetByStateForReviewerAsync(
        ApplicationState state,
        ReviewerScopeHint scope,
        int page,
        int pageSize,
        string? searchTerm = null);

    Task<(List<Application> Items, int TotalCount)> GetPendingAgreementPagedAsync(int page, int pageSize);

    /// <summary>
    /// Spec 016 / FR-012 — true when the application's applicant
    /// (via <c>Applicant.UserId</c>) shares at least one group id with
    /// <paramref name="reviewerGroupIds"/>. Used by detail-page authorization
    /// to mirror the listing predicate.
    /// </summary>
    Task<bool> ApplicantSharesAnyGroupAsync(
        int applicationId,
        IReadOnlyCollection<int> reviewerGroupIds,
        CancellationToken ct);

    Task AddAsync(Application application);
    Task UpdateAsync(Application application);
    Task SaveChangesAsync();
}
