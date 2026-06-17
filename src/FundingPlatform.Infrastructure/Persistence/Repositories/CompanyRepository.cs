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

    public async Task<IReadOnlyList<Company>> GetActiveByApplicantAsync(int applicantId, CancellationToken ct = default)
        => await _context.Companies
            .Where(c => c.ApplicantId == applicantId && c.ArchivedAt == null)
            .OrderBy(c => c.Name)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<Company>> GetAllByApplicantAsync(int applicantId, CancellationToken ct = default)
        => await _context.Companies
            .Where(c => c.ApplicantId == applicantId)
            .OrderBy(c => c.ArchivedAt == null ? 0 : 1)
            .ThenBy(c => c.Name)
            .ToListAsync(ct);

    public async Task<Company?> GetActiveByIdForApplicantAsync(int companyId, int applicantId, CancellationToken ct = default)
        => await _context.Companies
            .FirstOrDefaultAsync(
                c => c.Id == companyId && c.ApplicantId == applicantId && c.ArchivedAt == null, ct);

    public async Task<Company?> GetByIdAsync(int companyId, CancellationToken ct = default)
        => await _context.Companies.FirstOrDefaultAsync(c => c.Id == companyId, ct);

    public async Task<int> CountActiveExceptAsync(int applicantId, int exceptCompanyId, CancellationToken ct = default)
        => await _context.Companies
            .CountAsync(c => c.ApplicantId == applicantId && c.ArchivedAt == null && c.Id != exceptCompanyId, ct);

    public async Task AddAsync(Company company, CancellationToken ct = default)
        => await _context.Companies.AddAsync(company, ct);

    public async Task SaveChangesAsync(CancellationToken ct = default)
        => await _context.SaveChangesAsync(ct);
}
