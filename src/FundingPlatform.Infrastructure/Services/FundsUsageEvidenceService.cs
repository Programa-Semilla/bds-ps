// Spec 036 — see specs/036-funds-usage-evidence/contracts/interfaces.md and research D6/D9.

using System.Globalization;
using System.Text.Json;
using FundingPlatform.Application.Abstractions;
using FundingPlatform.Application.Abstractions.Storage;
using FundingPlatform.Application.FundsUsageEvidence;
using FundingPlatform.Domain.Entities;
using FundingPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
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
    private readonly ILogger<FundsUsageEvidenceService> _logger;

    public FundsUsageEvidenceService(
        AppDbContext db,
        IObjectStorage storage,
        IAdminAuditEventWriter audit,
        ILogger<FundsUsageEvidenceService> logger)
    {
        _db = db;
        _storage = storage;
        _audit = audit;
        _logger = logger;
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
            // No row was committed; the blob would otherwise leak (research D9).
            await DeleteBlobBestEffortAsync(key.Value, ct);
            throw;
        }

        // Audit written after the row commit (mirrors FundService.CreateAsync). A second
        // SaveChanges rather than one transaction: AddSqlServerDbContext enables the
        // retrying execution strategy, which forbids a raw user-initiated transaction,
        // and an execution-strategy wrapper would re-execute (re-adding the tracked row)
        // on a transient retry. This matches the shipping FundService pattern.
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

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Concurrent delete: another reviewer already removed this row (the
            // RowVersion token no longer matches). The item is already gone, so the
            // outcome the caller wanted holds — resolve harmlessly (US3 AS-3 / SC-003).
            _logger.LogInformation(
                "Funds-usage evidence {EvidenceId} was concurrently deleted; treating as already removed.", evidenceId);
        }
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
            _logger.LogWarning(
                "Funds-usage evidence {EvidenceId} has an unparseable blob key; download resolves to not-found.", evidenceId);
            return null;
        }

        try
        {
            var resolved = await _storage.ResolveServingHandleAsync(Category, key, ServingMode.BackendStream, ct);
            // Defensive: the category is wired to BackendStream, so this should always
            // be a BackendStreamHandle. Degrade to not-found rather than crash if a
            // serving-mode misconfiguration ever returns a different handle.
            if (resolved is not BackendStreamHandle handle)
            {
                _logger.LogWarning(
                    "Funds-usage evidence {EvidenceId} resolved to a non-backend-stream handle ({HandleType}).",
                    evidenceId, resolved.GetType().Name);
                return null;
            }

            return new FundsUsageEvidenceDownload(
                handle.Content, handle.ContentType ?? row.ContentType, row.OriginalFileName);
        }
        catch (ObjectNotFoundException)
        {
            _logger.LogWarning(
                "Funds-usage evidence {EvidenceId} row exists but its blob is missing; download resolves to not-found.", evidenceId);
            return null;
        }
    }

    private async Task DeleteBlobBestEffortAsync(string blobKey, CancellationToken ct)
    {
        try
        {
            await _storage.DeleteAsync(Category, ObjectKey.Parse(blobKey), ct);
        }
        catch (Exception ex)
        {
            // The blob may be malformed or already gone; the row is the source of
            // truth, so leaking is preferable to failing the mutation (research D9).
            // Log so operators can reconcile leaked blobs.
            _logger.LogWarning(ex,
                "Best-effort delete of funds-usage evidence blob {BlobKey} failed; it may be leaked.", blobKey);
        }
    }

    private static string ComposeDisplayName(string? firstName, string? lastName, string? email)
    {
        var full = $"{firstName} {lastName}".Trim();
        return full.Length > 0 ? full : (email ?? string.Empty);
    }
}
