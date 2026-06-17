// Spec 037 — see specs/037-applicant-companies/contracts/interfaces.md
// (ICompanyRepository).

using FundingPlatform.Domain.Entities;
using FundingPlatform.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace FundingPlatform.Infrastructure.Persistence.Repositories;

/// <summary>
/// Spec 037 — EF-backed <see cref="ICompanyRepository"/> over <see cref="AppDbContext"/>.
/// </summary>
public sealed class CompanyRepository : ICompanyRepository
{
    private readonly AppDbContext _context;

    public CompanyRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task<Company?> GetActiveByIdForApplicantAsync(int companyId, int applicantId, CancellationToken ct = default)
        => await _context.Companies
            .FirstOrDefaultAsync(
                c => c.Id == companyId && c.ApplicantId == applicantId && c.ArchivedAt == null, ct);
}
