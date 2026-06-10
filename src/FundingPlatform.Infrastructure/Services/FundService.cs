// Spec 029 — see specs/029-fund-entity/research.md D7 and
// contracts/ui-and-routes.md (Admin Fund management).

using System.Text.Json;
using FundingPlatform.Application.Abstractions;
using FundingPlatform.Application.Abstractions.Storage;
using FundingPlatform.Application.Funds;
using FundingPlatform.Domain.Entities;
using FundingPlatform.Domain.Enums;
using FundingPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FundingPlatform.Infrastructure.Services;

/// <summary>
/// Spec 029 / US1 — implements <see cref="IFundService"/>. Every mutation stages
/// an <c>AdminAuditEvent</c> (fund.*) via <see cref="IAdminAuditEventWriter"/> and
/// commits in the same UnitOfWork. Regulation blobs stream through
/// <see cref="IObjectStorage"/> under <see cref="FileCategory.FundRegulation"/>;
/// superseded blobs are deleted best-effort after the row is updated. Mirrors
/// <c>ProcessService</c> for transactional discipline.
/// </summary>
public sealed class FundService : IFundService
{
    private const FileCategory RegulationCategory = FileCategory.FundRegulation;

    private readonly AppDbContext _db;
    private readonly IObjectStorage _storage;
    private readonly IAdminAuditEventWriter _audit;

    public FundService(AppDbContext db, IObjectStorage storage, IAdminAuditEventWriter audit)
    {
        _db = db;
        _storage = storage;
        _audit = audit;
    }

    public async Task<IReadOnlyList<FundListRow>> ListAsync(FundStatus? statusFilter, CancellationToken ct)
    {
        var query = _db.Funds.AsNoTracking().AsQueryable();
        if (statusFilter is not null)
        {
            query = query.Where(f => f.Status == statusFilter);
        }

        return await query
            .OrderBy(f => f.Name)
            .Select(f => new FundListRow(
                f.Id,
                f.Name,
                f.Status,
                f.Processes.Count,
                f.RegulationBlobKey != null))
            .ToListAsync(ct);
    }

    public async Task<FundDetail?> GetDetailAsync(int id, CancellationToken ct)
    {
        return await _db.Funds.AsNoTracking()
            .Where(f => f.Id == id)
            .Select(f => new FundDetail(
                f.Id,
                f.Name,
                f.Description,
                f.Status,
                f.RegulationBlobKey != null,
                f.RegulationFileName,
                f.Processes
                    .OrderBy(p => p.Name)
                    .Select(p => new FundProcessRow(p.Id, p.Name, p.Status))
                    .ToList()))
            .FirstOrDefaultAsync(ct);
    }

    public async Task<int> CreateAsync(CreateFundCommand command, string actorUserId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentException.ThrowIfNullOrWhiteSpace(actorUserId);

        await EnsureNameAvailableAsync(command.Name, excludeFundId: null, ct);

        var fund = Fund.Create(command.Name, command.Description);
        _db.Funds.Add(fund);
        await _db.SaveChangesAsync(ct); // assign Id before building the regulation ObjectKey

        if (command.Regulation is not null)
        {
            await StoreRegulationAsync(fund, command.Regulation, actorUserId, ct);
        }

        await _audit.WriteAsync(
            AdminAuditEvent.ActionFundCreate, actorUserId,
            JsonSerializer.Serialize(new { fundId = fund.Id, name = fund.Name }), ct);
        await _db.SaveChangesAsync(ct);

        return fund.Id;
    }

    public async Task EditAsync(EditFundCommand command, string actorUserId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        var fund = await LoadAsync(command.FundId, ct);

        await EnsureNameAvailableAsync(command.Name, excludeFundId: fund.Id, ct);

        fund.Rename(command.Name);
        fund.EditDescription(command.Description);

        await _audit.WriteAsync(
            AdminAuditEvent.ActionFundEdit, actorUserId,
            JsonSerializer.Serialize(new { fundId = fund.Id, name = fund.Name }), ct);
        await _db.SaveChangesAsync(ct);
    }

    public async Task ArchiveAsync(int fundId, string actorUserId, CancellationToken ct)
    {
        var fund = await LoadAsync(fundId, ct);
        fund.Archive();
        await _audit.WriteAsync(
            AdminAuditEvent.ActionFundArchive, actorUserId,
            JsonSerializer.Serialize(new { fundId }), ct);
        await _db.SaveChangesAsync(ct);
    }

    public async Task ReactivateAsync(int fundId, string actorUserId, CancellationToken ct)
    {
        var fund = await LoadAsync(fundId, ct);
        fund.Reactivate();
        await _audit.WriteAsync(
            AdminAuditEvent.ActionFundReactivate, actorUserId,
            JsonSerializer.Serialize(new { fundId }), ct);
        await _db.SaveChangesAsync(ct);
    }

    public async Task SetRegulationAsync(SetFundRegulationCommand command, string actorUserId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        var fund = await LoadAsync(command.FundId, ct);

        await StoreRegulationAsync(fund, command.Regulation, actorUserId, ct);

        await _audit.WriteAsync(
            AdminAuditEvent.ActionFundRegulationSet, actorUserId,
            JsonSerializer.Serialize(new { fundId = fund.Id, fileName = command.Regulation.FileName }), ct);
        await _db.SaveChangesAsync(ct);
    }

    public async Task RemoveRegulationAsync(int fundId, string actorUserId, CancellationToken ct)
    {
        var fund = await LoadAsync(fundId, ct);
        if (!fund.HasRegulation)
        {
            return;
        }

        var oldKey = fund.RegulationBlobKey!;
        fund.RemoveRegulation();

        await _audit.WriteAsync(
            AdminAuditEvent.ActionFundRegulationRemove, actorUserId,
            JsonSerializer.Serialize(new { fundId }), ct);
        await _db.SaveChangesAsync(ct);

        await DeleteBlobBestEffortAsync(oldKey, ct);
    }

    // ---------------------------------------------------------------------

    private async Task StoreRegulationAsync(
        Fund fund, FundRegulationUpload upload, string actorUserId, CancellationToken ct)
    {
        var newKey = ObjectKey.Build(
            RegulationCategory,
            ownerSegment: "admin",
            entityId: fund.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            deterministicSuffix: Guid.NewGuid().ToString("N")[..16],
            extension: ".pdf");

        await _storage.UploadAsync(
            RegulationCategory, newKey, upload.Content, "application/pdf", upload.SizeBytes, ct);

        var supersededKey = fund.RegulationBlobKey;

        fund.SetRegulation(
            newKey.Value,
            upload.FileName,
            "application/pdf",
            upload.SizeBytes,
            actorUserId,
            DateTime.UtcNow);

        if (!string.IsNullOrWhiteSpace(supersededKey) &&
            !string.Equals(supersededKey, newKey.Value, StringComparison.Ordinal))
        {
            await DeleteBlobBestEffortAsync(supersededKey, ct);
        }
    }

    private async Task DeleteBlobBestEffortAsync(string blobKey, CancellationToken ct)
    {
        try
        {
            await _storage.DeleteAsync(RegulationCategory, ObjectKey.Parse(blobKey), ct);
        }
        catch
        {
            // The blob may be malformed or already gone; the row no longer
            // references it, so leaking is preferable to failing the mutation.
        }
    }

    private async Task<Fund> LoadAsync(int fundId, CancellationToken ct)
        => await _db.Funds.FirstOrDefaultAsync(f => f.Id == fundId, ct)
           ?? throw new KeyNotFoundException($"Fund {fundId} not found.");

    private async Task EnsureNameAvailableAsync(string name, int? excludeFundId, CancellationToken ct)
    {
        var trimmed = (name ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            return; // domain factory raises the required-name error
        }

        var collision = await _db.Funds
            .Where(f => f.Name == trimmed && (excludeFundId == null || f.Id != excludeFundId))
            .AnyAsync(ct);
        if (collision)
        {
            throw new DuplicateFundNameException(trimmed);
        }
    }
}
