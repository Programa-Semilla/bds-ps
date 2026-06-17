// Spec 037 — see specs/037-applicant-companies/contracts/interfaces.md
// (ICompanyAdministrationService) and research.md D4/D5.

using FundingPlatform.Application.Admin.Users.DTOs;
using FundingPlatform.Application.Errors;

namespace FundingPlatform.Application.Admin.Companies;

/// <summary>
/// Spec 037 / FR-005–FR-008 — admin-actor-scoped post-creation management of an
/// applicant's companies. Mirrors <c>IFundService</c>: the Infrastructure impl folds
/// DB access in, validates (per-applicant active-name uniqueness, D3; last-active
/// floor, D5), writes an <c>AdminAuditEvent</c> (<c>company.*</c>), and commits in a
/// single <c>SaveChangesAsync</c>. At-creation attach lives in
/// <c>UserAdministrationService</c> (co-commits with the Applicant, D4), not here.
/// </summary>
public interface ICompanyAdministrationService
{
    /// <summary>Returns the applicant's companies (active + archived) for the admin Edit card.</summary>
    Task<IReadOnlyList<CompanyDto>> ListAsync(int applicantId, CancellationToken ct = default);

    /// <summary>
    /// FR-005 — add a company to an existing applicant. Validates per-applicant active-name
    /// uniqueness (D3). Returns the new CompanyDto or a <see cref="UserFacingError"/>.
    /// </summary>
    Task<CompanyMutationResult> AddAsync(int applicantId, string name, string actorUserId, CancellationToken ct = default);

    /// <summary>FR-006 — rename. No-op (and no audit) when equal after trim. Uniqueness re-checked.</summary>
    Task<CompanyMutationResult> RenameAsync(int companyId, string newName, string actorUserId, CancellationToken ct = default);

    /// <summary>FR-007/FR-008 — soft archive. Refuses when it is the applicant's last active company.</summary>
    Task<CompanyMutationResult> ArchiveAsync(int companyId, string actorUserId, CancellationToken ct = default);

    /// <summary>FR-007 — unarchive. Refuses when the name now collides with an active company.</summary>
    Task<CompanyMutationResult> UnarchiveAsync(int companyId, string actorUserId, CancellationToken ct = default);
}

/// <summary>
/// Spec 037 — result envelope: a successful mutation carries the resulting
/// <see cref="CompanyDto"/>; a rejected one carries a <see cref="UserFacingError"/>.
/// </summary>
public sealed record CompanyMutationResult(CompanyDto? Company, UserFacingError? Error)
{
    public bool Succeeded => Error is null;

    public static CompanyMutationResult Ok(CompanyDto company) => new(company, null);
    public static CompanyMutationResult Fail(UserFacingError error) => new(null, error);
}
