// Spec 036 — see specs/036-funds-usage-evidence/contracts/interfaces.md and research D6/D9.

using System.Globalization;
using System.Text.Json;
using FundingPlatform.Application.Abstractions;
using FundingPlatform.Application.Abstractions.Storage;
using FundingPlatform.Application.FundsUsageEvidence;
using FundingPlatform.Domain.Entities;
using FundingPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using EvidenceEntity = FundingPlatform.Domain.Entities.FundsUsageEvidence;

namespace FundingPlatform.Infrastructure.Services;

/// <summary>
/// Spec 036 — implements <see cref="IFundsUsageEvidenceService"/>. Mirrors
/// <c>FundService</c> for transactional discipline: every mutation stages an
/// <c>AdminAuditEvent</c> (<c>funds_evidence.*</c>) via <see cref="IAdminAuditEventWriter"/>
/// and commits on the shared <see cref="AppDbContext"/>; blobs stream through
/// <see cref="IObjectStorage"/> under <see cref="FileCategory.FundsUsageEvidence"/>.
/// Group-scope + role authorization is the controller's responsibility (it holds
/// the HTTP principal); this service trusts the caller for scope.
/// </summary>
public sealed class FundsUsageEvidenceService : IFundsUsageEvidenceService
{
    private const FileCategory Category = FileCategory.FundsUsageEvidence;

    private readonly AppDbContext _db;
    private readonly IObjectStorage _storage;
    private readonly IAdminAuditEventWriter _audit;

    public FundsUsageEvidenceService(AppDbContext db, IObjectStorage storage, IAdminAuditEventWriter audit)
    {
        _db = db;
        _storage = storage;
        _audit = audit;
    }

    public async Task<IReadOnlyList<FundsUsageEvidenceListItem>> ListAsync(int applicationId, CancellationToken ct)
    {
        var rows = await _db.FundsUsageEvidence.AsNoTracking()
            .Where(e => e.ApplicationId == applicationId)
            .OrderByDescending(e => e.UploadedAt).ThenByDescending(e => e.Id)
            .Join(
                _db.Users.AsNoTracking(),
                e => e.UploadedByUserId,
                u => u.Id,
                (e, u) => new
                {
                    e.Id,
                    e.OriginalFileName,
                    e.Note,
                    e.UploadedAt,
                    e.FileSize,
                    e.ContentType,
                    u.FirstName,
                    u.LastName,
                    u.Email,
                })
            .ToListAsync(ct);

        return rows
            .Select(r => new FundsUsageEvidenceListItem(
                r.Id,
                r.OriginalFileName,
                r.Note,
                ComposeDisplayName(r.FirstName, r.LastName, r.Email),
                r.UploadedAt,
                r.FileSize,
                r.ContentType))
            .ToList();
    }

    public async Task<int> UploadAsync(UploadFundsUsageEvidenceCommand cmd, string actorUserId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(cmd);
        ArgumentException.ThrowIfNullOrWhiteSpace(actorUserId);

        // Tracked load — State is a scalar; the domain factory enforces the
        // AgreementExecuted gate (FR-001).
        var application = await _db.Applications.FirstOrDefaultAsync(a => a.Id == cmd.ApplicationId, ct)
            ?? throw new KeyNotFoundException($"Application {cmd.ApplicationId} not found.");

        var ext = Path.GetExtension(cmd.OriginalFileName);
        var key = ObjectKey.Build(
            Category,
            ownerSegment: $"application/{cmd.ApplicationId.ToString(CultureInfo.InvariantCulture)}",
            entityId: cmd.ApplicationId.ToString(CultureInfo.InvariantCulture),
            deterministicSuffix: Guid.NewGuid().ToString("N")[..16],
            extension: string.IsNullOrWhiteSpace(ext) ? null : ext);

        await _storage.UploadAsync(Category, key, cmd.Content, cmd.ContentType, cmd.FileSize, ct);

        EvidenceEntity evidence;
        try
        {
            evidence = EvidenceEntity.CreateForExecutedApplication(
                application, actorUserId, cmd.OriginalFileName, key.Value, cmd.FileSize, cmd.ContentType, cmd.Note);
            _db.FundsUsageEvidence.Add(evidence);
            await _db.SaveChangesAsync(ct); // assigns Id before the audit payload references it
        }
        catch
        {
            // No row was committed; the blob would otherwise leak (FR / research D9).
            await DeleteBlobBestEffortAsync(key.Value, ct);
            throw;
        }

        await _audit.WriteAsync(
            AdminAuditEvent.FundsEvidenceUploaded, actorUserId,
            JsonSerializer.Serialize(new { applicationId = cmd.ApplicationId, evidenceId = evidence.Id, fileName = cmd.OriginalFileName }),
            ct);
        await _db.SaveChangesAsync(ct);

        return evidence.Id;
    }

    public async Task EditNoteAsync(int evidenceId, string? note, string actorUserId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actorUserId);

        var evidence = await _db.FundsUsageEvidence.FirstOrDefaultAsync(e => e.Id == evidenceId, ct)
            ?? throw new KeyNotFoundException($"FundsUsageEvidence {evidenceId} not found.");

        evidence.EditNote(note);

        await _audit.WriteAsync(
            AdminAuditEvent.FundsEvidenceNoteEdited, actorUserId,
            JsonSerializer.Serialize(new { applicationId = evidence.ApplicationId, evidenceId = evidence.Id, fileName = evidence.OriginalFileName }),
            ct);
        await _db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(int evidenceId, string actorUserId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actorUserId);

        var evidence = await _db.FundsUsageEvidence.FirstOrDefaultAsync(e => e.Id == evidenceId, ct)
            ?? throw new KeyNotFoundException($"FundsUsageEvidence {evidenceId} not found.");

        var blobKey = evidence.BlobKey;
        var applicationId = evidence.ApplicationId;
        var fileName = evidence.OriginalFileName;

        await DeleteBlobBestEffortAsync(blobKey, ct);

        _db.FundsUsageEvidence.Remove(evidence);

        await _audit.WriteAsync(
            AdminAuditEvent.FundsEvidenceDeleted, actorUserId,
            JsonSerializer.Serialize(new { applicationId, evidenceId, fileName }),
            ct);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<FundsUsageEvidenceDownload?> OpenForDownloadAsync(int evidenceId, CancellationToken ct)
    {
        var row = await _db.FundsUsageEvidence.AsNoTracking()
            .Where(e => e.Id == evidenceId)
            .Select(e => new { e.BlobKey, e.ContentType, e.OriginalFileName })
            .FirstOrDefaultAsync(ct);

        if (row is null)
        {
            return null;
        }

        ObjectKey key;
        try
        {
            key = ObjectKey.Parse(row.BlobKey);
        }
        catch
        {
            return null;
        }

        try
        {
            var resolved = await _storage.ResolveServingHandleAsync(Category, key, ServingMode.BackendStream, ct);
            var handle = (BackendStreamHandle)resolved;
            return new FundsUsageEvidenceDownload(
                handle.Content, handle.ContentType ?? row.ContentType, row.OriginalFileName);
        }
        catch (ObjectNotFoundException)
        {
            return null;
        }
    }

    private async Task DeleteBlobBestEffortAsync(string blobKey, CancellationToken ct)
    {
        try
        {
            await _storage.DeleteAsync(Category, ObjectKey.Parse(blobKey), ct);
        }
        catch
        {
            // The blob may be malformed or already gone; the row is the source of
            // truth, so leaking is preferable to failing the mutation (research D9).
        }
    }

    private static string ComposeDisplayName(string? firstName, string? lastName, string? email)
    {
        var full = $"{firstName} {lastName}".Trim();
        return full.Length > 0 ? full : (email ?? string.Empty);
    }
}
