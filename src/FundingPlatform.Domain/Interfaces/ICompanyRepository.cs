// Spec 037 — see specs/037-applicant-companies/contracts/interfaces.md
// (ICompanyRepository). Lives under Domain/Interfaces to match the existing
// repository-interface convention (the tasks.md path Domain/Repositories is a
// documented deviation — the codebase keeps all repo interfaces here).

using FundingPlatform.Domain.Entities;

namespace FundingPlatform.Domain.Interfaces;

/// <summary>
/// Spec 037 — company-resolution seam used by the applicant application-create path.
/// The Infrastructure impl (<c>CompanyRepository</c>) sits over <c>AppDbContext</c>.
///
/// Other company reads (admin list, draft-reselect lookup, floor count) are folded
/// directly into their services/controllers via <c>AppDbContext</c>, matching the
/// prevailing spec-036 <c>FundService</c> style — so this interface intentionally
/// carries only the one seam the Application layer needs.
/// </summary>
public interface ICompanyRepository
{
    /// <summary>
    /// Ownership + active resolution for selection validation. Returns null when
    /// the company is not owned by the applicant or is archived (FR-018/019).
    /// </summary>
    Task<Company?> GetActiveByIdForApplicantAsync(int companyId, int applicantId, CancellationToken ct = default);
}
