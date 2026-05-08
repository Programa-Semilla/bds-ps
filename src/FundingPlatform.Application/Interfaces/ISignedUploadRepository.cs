using FundingPlatform.Application.DTOs;
using FundingPlatform.Domain.Entities;

namespace FundingPlatform.Application.Interfaces;

public interface ISignedUploadRepository
{
    /// <summary>
    /// Loads a SignedUpload with its FundingAgreement + Application + Applicant hydrated,
    /// for authorization and action routing.
    /// </summary>
    Task<SignedUpload?> GetByIdWithParentAsync(int signedUploadId);

    /// <summary>
    /// Paged reviewer-inbox projection: applications whose latest signed upload is Pending.
    /// Admins see all (FR-015); reviewers see only those whose applicant shares
    /// at least one group with them (FR-013, NFR-001 — predicate composed at
    /// the EF query level).
    /// </summary>
    Task<(IReadOnlyList<SigningInboxRowDto> Rows, int TotalCount)> GetPendingInboxAsync(
        string? reviewerUserId,
        bool isAdmin,
        IReadOnlyCollection<int> reviewerGroupIds,
        int page,
        int pageSize);

    Task SaveChangesAsync();
}
