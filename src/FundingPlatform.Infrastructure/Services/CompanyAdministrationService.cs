// Spec 037 — see specs/037-applicant-companies/contracts/interfaces.md
// (ICompanyAdministrationService) and research.md D3/D4/D5/D10.

using System.Text.Json;
using FundingPlatform.Application.Abstractions;
using FundingPlatform.Application.Admin.Companies;
using FundingPlatform.Application.Admin.Users.DTOs;
using FundingPlatform.Application.Errors;
using FundingPlatform.Domain.Entities;
using FundingPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FundingPlatform.Infrastructure.Services;

/// <summary>
/// Spec 037 / US2 — implements <see cref="ICompanyAdministrationService"/>. Mirrors
/// <c>FundService</c>: folds DB access in, validates, stages a <c>company.*</c>
/// <c>AdminAuditEvent</c> via <see cref="IAdminAuditEventWriter"/>, and commits in a
/// single <c>SaveChangesAsync</c>. The namespace is <c>…Services</c> (not a
/// <c>…Companies</c> sub-namespace) to avoid the type-vs-namespace clash with the
/// <see cref="Company"/> domain entity (spec-036 gotcha).
/// </summary>
public sealed class CompanyAdministrationService : ICompanyAdministrationService
{
    private readonly AppDbContext _db;
    private readonly IAdminAuditEventWriter _audit;

    public CompanyAdministrationService(AppDbContext db, IAdminAuditEventWriter audit)
    {
        _db = db;
        _audit = audit;
    }

    public async Task<IReadOnlyList<CompanyDto>> ListAsync(int applicantId, CancellationToken ct = default)
    {
        return await _db.Companies.AsNoTracking()
            .Where(c => c.ApplicantId == applicantId)
            .OrderBy(c => c.ArchivedAt == null ? 0 : 1)
            .ThenBy(c => c.Name)
            .Select(c => new CompanyDto(c.Id, c.Name, c.ArchivedAt != null))
            .ToListAsync(ct);
    }

    public async Task<CompanyMutationResult> AddAsync(
        int applicantId, string name, string actorUserId, CancellationToken ct = default)
    {
        Company company;
        try
        {
            company = new Company(applicantId, name);
        }
        catch (ArgumentException ex)
        {
            return CompanyMutationResult.Fail(MapNameError(ex));
        }

        if (await ActiveNameTakenAsync(applicantId, company.Name, excludeCompanyId: null, ct))
        {
            return CompanyMutationResult.Fail(UserFacingError.From(UserFacingErrorCode.CompanyNameDuplicate));
        }

        _db.Companies.Add(company);
        await _audit.WriteAsync(
            AdminAuditEvent.ActionCompanyCreate, actorUserId,
            JsonSerializer.Serialize(new { companyId = 0, applicantId, name = company.Name }), ct);
        if (!await TrySaveAsync(ct))
        {
            // FR-003 — a concurrent add of the same active name raced past the
            // app-level pre-check and tripped the filtered unique index.
            return CompanyMutationResult.Fail(UserFacingError.From(UserFacingErrorCode.CompanyNameDuplicate));
        }

        return CompanyMutationResult.Ok(new CompanyDto(company.Id, company.Name, IsArchived: false));
    }

    public async Task<CompanyMutationResult> RenameAsync(
        int companyId, string newName, string actorUserId, CancellationToken ct = default)
    {
        var company = await _db.Companies.FirstOrDefaultAsync(c => c.Id == companyId, ct);
        if (company is null)
        {
            return CompanyMutationResult.Fail(UserFacingError.From(UserFacingErrorCode.CompanyInvalid));
        }

        var oldName = company.Name;
        string trimmed;
        try
        {
            // Validate the new name shape up front (so a too-long/blank rename fails
            // before the uniqueness probe), then apply via the entity.
            trimmed = NewCompanyName(newName);
        }
        catch (ArgumentException ex)
        {
            return CompanyMutationResult.Fail(MapNameError(ex));
        }

        // No-op (and no audit) when equal after trim.
        if (string.Equals(trimmed, oldName, StringComparison.Ordinal))
        {
            return CompanyMutationResult.Ok(new CompanyDto(company.Id, company.Name, company.ArchivedAt != null));
        }

        if (company.ArchivedAt == null
            && await ActiveNameTakenAsync(company.ApplicantId, trimmed, excludeCompanyId: company.Id, ct))
        {
            return CompanyMutationResult.Fail(UserFacingError.From(UserFacingErrorCode.CompanyNameDuplicate));
        }

        company.Rename(trimmed);
        await _audit.WriteAsync(
            AdminAuditEvent.ActionCompanyRename, actorUserId,
            JsonSerializer.Serialize(new { companyId = company.Id, oldName, newName = company.Name }), ct);
        if (!await TrySaveAsync(ct))
        {
            return CompanyMutationResult.Fail(UserFacingError.From(UserFacingErrorCode.CompanyNameDuplicate));
        }

        return CompanyMutationResult.Ok(new CompanyDto(company.Id, company.Name, company.ArchivedAt != null));
    }

    public async Task<CompanyMutationResult> ArchiveAsync(
        int companyId, string actorUserId, CancellationToken ct = default)
    {
        var company = await _db.Companies.FirstOrDefaultAsync(c => c.Id == companyId, ct);
        if (company is null)
        {
            return CompanyMutationResult.Fail(UserFacingError.From(UserFacingErrorCode.CompanyInvalid));
        }

        if (company.ArchivedAt != null)
        {
            // Idempotent: already archived.
            return CompanyMutationResult.Ok(new CompanyDto(company.Id, company.Name, IsArchived: true));
        }

        // FR-008 — refuse to archive the applicant's only active company. This is a
        // read-then-write check: two near-simultaneous archives of an applicant's last
        // two active companies could in principle both pass and reach zero active. That
        // TOCTOU window is accepted as a known low-probability limitation, matching the
        // analogous last-active-admin floor in UserAdministrationService (the retrying
        // execution strategy forbids a raw transaction, and a provider-specific guarded
        // UPDATE is not supported by the InMemory provider the integration tests use).
        var otherActive = await _db.Companies.CountAsync(
            c => c.ApplicantId == company.ApplicantId && c.ArchivedAt == null && c.Id != company.Id, ct);
        if (otherActive == 0)
        {
            return CompanyMutationResult.Fail(
                UserFacingError.From(UserFacingErrorCode.CompanyArchiveLastActive));
        }

        company.Archive();
        await _audit.WriteAsync(
            AdminAuditEvent.ActionCompanyArchive, actorUserId,
            JsonSerializer.Serialize(new { companyId = company.Id, name = company.Name }), ct);
        await _db.SaveChangesAsync(ct);

        return CompanyMutationResult.Ok(new CompanyDto(company.Id, company.Name, IsArchived: true));
    }

    public async Task<CompanyMutationResult> UnarchiveAsync(
        int companyId, string actorUserId, CancellationToken ct = default)
    {
        var company = await _db.Companies.FirstOrDefaultAsync(c => c.Id == companyId, ct);
        if (company is null)
        {
            return CompanyMutationResult.Fail(UserFacingError.From(UserFacingErrorCode.CompanyInvalid));
        }

        if (company.ArchivedAt != null
            && await ActiveNameTakenAsync(company.ApplicantId, company.Name, excludeCompanyId: company.Id, ct))
        {
            return CompanyMutationResult.Fail(
                UserFacingError.From(UserFacingErrorCode.CompanyUnarchiveNameCollision));
        }

        company.Unarchive();
        await _audit.WriteAsync(
            AdminAuditEvent.ActionCompanyUnarchive, actorUserId,
            JsonSerializer.Serialize(new { companyId = company.Id, name = company.Name }), ct);
        if (!await TrySaveAsync(ct))
        {
            return CompanyMutationResult.Fail(UserFacingError.From(UserFacingErrorCode.CompanyUnarchiveNameCollision));
        }

        return CompanyMutationResult.Ok(new CompanyDto(company.Id, company.Name, IsArchived: false));
    }

    // ---------------------------------------------------------------------

    /// <summary>
    /// D3 — app-level accent/case-insensitive active-name uniqueness probe. Loads the
    /// applicant's active names and compares under es-CR folding (the DB filtered index
    /// is the exact/case race backstop).
    /// </summary>
    private async Task<bool> ActiveNameTakenAsync(
        int applicantId, string candidateName, int? excludeCompanyId, CancellationToken ct)
    {
        var activeNames = await _db.Companies.AsNoTracking()
            .Where(c => c.ApplicantId == applicantId
                && c.ArchivedAt == null
                && (excludeCompanyId == null || c.Id != excludeCompanyId))
            .Select(c => c.Name)
            .ToListAsync(ct);

        return activeNames.Any(n => CompanyNameNormalizer.AreEquivalent(n, candidateName));
    }

    /// <summary>
    /// Commits the staged mutation + audit. Returns false when the filtered unique
    /// index (UX_Companies_ApplicantId_Name) rejects a concurrent active-name collision
    /// that raced past the app-level pre-check (D3) — the caller maps this to the right
    /// es-CR duplicate/collision message.
    /// </summary>
    private async Task<bool> TrySaveAsync(CancellationToken ct)
    {
        try
        {
            await _db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException ex) when (
            ex.GetBaseException().Message.Contains("UX_Companies_ApplicantId_Name", StringComparison.Ordinal))
        {
            return false;
        }
    }

    /// <summary>Validates a company name's shape via the entity, returning the trimmed value.</summary>
    private static string NewCompanyName(string name) => new Company(0, name).Name;

    private static UserFacingError MapNameError(ArgumentException ex)
    {
        var reason = ex.Data[Item.ValidationReasonKey] as string;
        var code = reason switch
        {
            Company.NameTooLongReason => UserFacingErrorCode.CompanyNameTooLong,
            _ => UserFacingErrorCode.CompanyNameRequired,
        };
        return UserFacingError.From(code);
    }
}
