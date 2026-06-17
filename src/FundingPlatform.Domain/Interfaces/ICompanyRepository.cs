// Spec 037 — see specs/037-applicant-companies/contracts/interfaces.md
// (ICompanyRepository). Lives under Domain/Interfaces to match the existing
// repository-interface convention (the tasks.md path Domain/Repositories is a
// documented deviation — the codebase keeps all repo interfaces here).

using FundingPlatform.Domain.Entities;

namespace FundingPlatform.Domain.Interfaces;

/// <summary>
/// Spec 037 — read/write access to the <see cref="Company"/> aggregate. The
/// Infrastructure impl (<c>CompanyRepository</c>) sits over <c>AppDbContext</c>.
/// </summary>
public interface ICompanyRepository
{
    /// <summary>Active companies for an applicant, ordered by Name (dropdown + admin list source).</summary>
    Task<IReadOnlyList<Company>> GetActiveByApplicantAsync(int applicantId, CancellationToken ct = default);

    /// <summary>All companies (active + archived) for an applicant — admin Edit management card.</summary>
    Task<IReadOnlyList<Company>> GetAllByApplicantAsync(int applicantId, CancellationToken ct = default);

    /// <summary>
    /// Ownership + active resolution for selection validation. Returns null when
    /// the company is not owned by the applicant or is archived (FR-018/019).
    /// </summary>
    Task<Company?> GetActiveByIdForApplicantAsync(int companyId, int applicantId, CancellationToken ct = default);

    Task<Company?> GetByIdAsync(int companyId, CancellationToken ct = default);

    /// <summary>Count of OTHER active companies for the applicant (floor check excludes the candidate).</summary>
    Task<int> CountActiveExceptAsync(int applicantId, int exceptCompanyId, CancellationToken ct = default);

    Task AddAsync(Company company, CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
}
