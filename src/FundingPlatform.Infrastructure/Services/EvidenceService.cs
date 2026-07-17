// Spec 047 — see specs/047-evidence-graph-required-docs/contracts/interfaces.md and research D1/D2/D4/D8.

using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using FundingPlatform.Application.Abstractions;
using FundingPlatform.Application.Abstractions.Storage;
using FundingPlatform.Application.Admin.Users.DTOs;
using FundingPlatform.Application.Evidence;
using FundingPlatform.Domain.Entities;
using FundingPlatform.Domain.Enums;
using FundingPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using EvidenceEntity = FundingPlatform.Domain.Entities.Evidence;

namespace FundingPlatform.Infrastructure.Services;

/// <summary>
/// Spec 047 — implements <see cref="IEvidenceService"/>. Mirrors <c>DisbursementService</c> for
/// storage + the two-SaveChanges audit discipline. The append-only <see cref="EvidenceVersion"/>
/// chain is the audit history; the parent <see cref="EvidenceEntity"/> row carries the current
/// denormalized values. Group-scope + role authorization is the controller's job.
/// </summary>
public sealed class EvidenceService : IEvidenceService
{
    private const FileCategory Category = FileCategory.Evidence;

    private readonly AppDbContext _db;
    private readonly IObjectStorage _storage;
    private readonly IAdminAuditEventWriter _audit;
    private readonly ILogger<EvidenceService> _logger;

    public EvidenceService(
        AppDbContext db,
        IObjectStorage storage,
        IAdminAuditEventWriter audit,
        ILogger<EvidenceService> logger)
    {
        _db = db;
        _storage = storage;
        _audit = audit;
        _logger = logger;
    }

    // ---------------------------------------------------------------- reads

    public async Task<IReadOnlyList<EvidenceSummary>> ListForApplicationAsync(int applicationId, CancellationToken ct)
    {
        var rows = await _db.Evidence.AsNoTracking()
            .Where(e => e.ApplicationId == applicationId)
            .OrderByDescending(e => e.UploadedAtUtc).ThenByDescending(e => e.Id)
            .Select(e => new
            {
                e.Id,
                e.Type,
                e.Amount,
                e.Currency,
                e.DocumentReferenceNumber,
                e.DocumentDate,
                SupplierName = e.SupplierId != null
                    ? _db.Suppliers.Where(s => s.Id == e.SupplierId).Select(s => s.Name).FirstOrDefault()
                    : null,
                e.OriginalFileName,
                e.UploadedByUserId,
                e.UploadedAtUtc,
                VersionCount = _db.EvidenceVersions.Count(v => v.EvidenceId == e.Id),
                AllocatedTotal = _db.EvidenceLineAllocations
                    .Where(a => a.EvidenceId == e.Id).Sum(a => (decimal?)a.Amount) ?? 0m,
                AllocatedLineCount = _db.EvidenceLineAllocations.Count(a => a.EvidenceId == e.Id),
            })
            .ToListAsync(ct);

        var userIds = rows.Select(r => r.UploadedByUserId).Distinct().ToList();
        var names = await ResolveDisplayNamesAsync(userIds, ct);

        return rows.Select(r => new EvidenceSummary(
            r.Id, r.Type, r.Amount, r.Currency, r.DocumentReferenceNumber, r.DocumentDate,
            r.SupplierName, r.OriginalFileName, Lookup(names, r.UploadedByUserId), r.UploadedAtUtc,
            r.VersionCount, r.AllocatedTotal, r.AllocatedLineCount)).ToList();
    }

    public async Task<EvidenceDetail?> GetAsync(int applicationId, int evidenceId, CancellationToken ct)
    {
        var e = await _db.Evidence.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == evidenceId && x.ApplicationId == applicationId, ct);
        if (e is null)
        {
            return null;
        }

        var supplierName = e.SupplierId is null ? null : await _db.Suppliers.AsNoTracking()
            .Where(s => s.Id == e.SupplierId).Select(s => s.Name).FirstOrDefaultAsync(ct);

        var allocations = await _db.EvidenceLineAllocations.AsNoTracking()
            .Where(a => a.EvidenceId == evidenceId)
            .Join(_db.Items.AsNoTracking(), a => a.ItemId, i => i.Id,
                (a, i) => new { a.ItemId, i.LineCode, i.ProductName, a.Amount })
            .ToListAsync(ct);

        var allocationRows = allocations
            .Select(a => new EvidenceLineAllocationRow(a.ItemId, LineLabel(a.LineCode, a.ProductName, a.ItemId), a.Amount))
            .ToList();

        var versions = await VersionRowsAsync(evidenceId, ct);
        var uploadedBy = await ResolveDisplayNameAsync(e.UploadedByUserId, ct);

        return new EvidenceDetail(
            e.Id, e.ApplicationId, e.Type, e.DisbursementId, e.Amount, e.Currency,
            e.DocumentReferenceNumber, e.DocumentDate, supplierName, e.OriginalFileName,
            uploadedBy, e.UploadedAtUtc, allocationRows, versions);
    }

    public async Task<IReadOnlyList<EvidenceVersionRow>> GetVersionsAsync(int applicationId, int evidenceId, CancellationToken ct)
    {
        var belongs = await _db.Evidence.AsNoTracking()
            .AnyAsync(e => e.Id == evidenceId && e.ApplicationId == applicationId, ct);
        return belongs ? await VersionRowsAsync(evidenceId, ct) : [];
    }

    public async Task<EvidenceDownload?> OpenForDownloadAsync(int applicationId, int evidenceId, int? versionNumber, CancellationToken ct)
    {
        var belongs = await _db.Evidence.AsNoTracking()
            .AnyAsync(e => e.Id == evidenceId && e.ApplicationId == applicationId, ct);
        if (!belongs)
        {
            return null;
        }

        var version = await _db.EvidenceVersions.AsNoTracking()
            .Where(v => v.EvidenceId == evidenceId && (versionNumber == null ? v.IsCurrent : v.VersionNumber == versionNumber))
            .Select(v => new { v.BlobKey, v.ContentType, v.OriginalFileName })
            .FirstOrDefaultAsync(ct);
        if (version is null)
        {
            return null;
        }

        ObjectKey key;
        try
        {
            key = ObjectKey.Parse(version.BlobKey);
        }
        catch
        {
            _logger.LogWarning("Evidence {EvidenceId} version has an unparseable blob key.", evidenceId);
            return null;
        }

        try
        {
            var resolved = await _storage.ResolveServingHandleAsync(Category, key, ServingMode.BackendStream, ct);
            if (resolved is not BackendStreamHandle handle)
            {
                _logger.LogWarning("Evidence {EvidenceId} resolved to a non-backend-stream handle.", evidenceId);
                return null;
            }
            return new EvidenceDownload(handle.Content, handle.ContentType ?? version.ContentType, version.OriginalFileName);
        }
        catch (ObjectNotFoundException)
        {
            _logger.LogWarning("Evidence {EvidenceId} row exists but its blob is missing.", evidenceId);
            return null;
        }
    }

    // ---------------------------------------------------------------- attach

    public async Task<Result<int>> AttachAsync(AttachEvidenceCommand cmd, string actorUserId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(cmd);
        ArgumentException.ThrowIfNullOrWhiteSpace(actorUserId);

        var app = await _db.Applications.AsNoTracking()
            .Select(a => new { a.Id, a.State })
            .FirstOrDefaultAsync(a => a.Id == cmd.ApplicationId, ct);
        if (app is null)
        {
            return Result<int>.Failure(new DomainError(EvidenceReasons.Codes.ApplicationNotFound, null, EvidenceReasons.ApplicationNotFound));
        }
        if (app.State != ApplicationState.AgreementExecuted)
        {
            return Result<int>.Failure(new DomainError(EvidenceReasons.Codes.NotExecuted, null, EvidenceReasons.NotExecuted));
        }

        var validation = await ValidateWriteAsync(cmd.ApplicationId, cmd.Amount, cmd.Currency, cmd.DocumentReferenceNumber,
            cmd.DisbursementId, cmd.Lines, requireLineOrDisbursement: true, ct);
        if (validation is not null)
        {
            return Result<int>.Failure(validation);
        }

        // Buffer already at position 0 (controller). Hash first, then upload.
        var fileHash = await ComputeSha256Async(cmd.Content, ct);

        var ext = Path.GetExtension(cmd.FileName);
        var key = ObjectKey.Build(
            Category,
            ownerSegment: $"application/{cmd.ApplicationId.ToString(CultureInfo.InvariantCulture)}",
            entityId: "evidence",
            deterministicSuffix: Guid.NewGuid().ToString("N")[..16],
            extension: string.IsNullOrWhiteSpace(ext) ? null : ext);

        await _storage.UploadAsync(Category, key, cmd.Content, cmd.ContentType, cmd.FileSize, ct);

        EvidenceEntity evidence;
        try
        {
            evidence = EvidenceEntity.Attach(
                cmd.ApplicationId, cmd.Type, cmd.DisbursementId, cmd.Amount, cmd.Currency,
                cmd.DocumentReferenceNumber, cmd.DocumentDate, cmd.SupplierId,
                cmd.FileName, key.Value, cmd.FileSize, cmd.ContentType, fileHash, actorUserId);
            _db.Evidence.Add(evidence);
            await _db.SaveChangesAsync(ct); // assigns evidence.Id + v1; commits the node + version
        }
        catch
        {
            await DeleteBlobBestEffortAsync(key.Value, ct);
            throw;
        }

        // Persist the per-line allocation rows now that the evidence id exists.
        foreach (var l in cmd.Lines)
        {
            _db.EvidenceLineAllocations.Add(EvidenceLineAllocation.For(evidence.Id, l.ItemId, l.Amount));
        }

        await WriteAuditAsync(AdminAuditEvent.EvidenceAttached, actorUserId, evidence.Id, cmd.ApplicationId, cmd.Type, ct);

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result<int>.Failure(new DomainError(EvidenceReasons.Codes.Concurrency, null, EvidenceReasons.Concurrency));
        }

        return Result<int>.Success(evidence.Id);
    }

    // ---------------------------------------------------------------- replace (US4)

    public async Task<Result> ReplaceAsync(ReplaceEvidenceCommand cmd, string actorUserId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(cmd);
        ArgumentException.ThrowIfNullOrWhiteSpace(actorUserId);

        if (string.IsNullOrWhiteSpace(cmd.Reason))
        {
            return Result.Failure(new DomainError(EvidenceReasons.Codes.ReasonRequired, nameof(cmd.Reason), EvidenceReasons.ReasonRequired));
        }

        var evidence = await _db.Evidence
            .Include(e => e.Versions)
            .FirstOrDefaultAsync(e => e.Id == cmd.EvidenceId && e.ApplicationId == cmd.ApplicationId, ct);
        if (evidence is null)
        {
            return Result.Failure(new DomainError(EvidenceReasons.Codes.NotFound, null, EvidenceReasons.NotFound));
        }

        var validation = await ValidateWriteAsync(cmd.ApplicationId, cmd.Amount, cmd.Currency, cmd.DocumentReferenceNumber,
            evidence.DisbursementId, lines: null, requireLineOrDisbursement: false, ct);
        if (validation is not null)
        {
            return Result.Failure(validation);
        }

        var lockError = await EnsureEvidenceUnlockedAsync(cmd.EvidenceId, ct);
        if (lockError is not null)
        {
            return Result.Failure(lockError);
        }

        // File replace (optional) — hash + upload a new blob; otherwise carry the current pointer.
        string blobKey = evidence.BlobKey;
        string originalFileName = evidence.OriginalFileName;
        long fileSize = evidence.FileSize;
        string contentType = evidence.ContentType;
        string fileHash = evidence.FileHash;
        string? oldBlobKey = null;

        if (cmd.Content is not null && cmd.FileName is not null && cmd.ContentType is not null && cmd.FileSize is > 0)
        {
            fileHash = await ComputeSha256Async(cmd.Content, ct);
            var ext = Path.GetExtension(cmd.FileName);
            var key = ObjectKey.Build(
                Category,
                ownerSegment: $"application/{cmd.ApplicationId.ToString(CultureInfo.InvariantCulture)}",
                entityId: "evidence",
                deterministicSuffix: Guid.NewGuid().ToString("N")[..16],
                extension: string.IsNullOrWhiteSpace(ext) ? null : ext);
            await _storage.UploadAsync(Category, key, cmd.Content, cmd.ContentType, cmd.FileSize.Value, ct);
            oldBlobKey = evidence.BlobKey;
            blobKey = key.Value;
            originalFileName = cmd.FileName;
            fileSize = cmd.FileSize.Value;
            contentType = cmd.ContentType;
        }

        try
        {
            evidence.ReplaceCurrent(cmd.Amount, cmd.Currency, cmd.DocumentReferenceNumber, cmd.DocumentDate,
                originalFileName, blobKey, fileSize, contentType, fileHash, cmd.Reason, actorUserId);
            await _db.SaveChangesAsync(ct); // commits the superseded + new version + updated node
        }
        catch (DbUpdateConcurrencyException)
        {
            if (oldBlobKey is not null && blobKey != oldBlobKey)
            {
                await DeleteBlobBestEffortAsync(blobKey, ct);
            }
            return Result.Failure(new DomainError(EvidenceReasons.Codes.Concurrency, null, EvidenceReasons.Concurrency));
        }
        catch
        {
            if (oldBlobKey is not null && blobKey != oldBlobKey)
            {
                await DeleteBlobBestEffortAsync(blobKey, ct);
            }
            throw;
        }

        // The node now points at the new blob; the old blob is retained as the superseded version's
        // pointer (FR-021 — prior file remains downloadable), so it is NOT deleted here.

        await WriteAuditAsync(AdminAuditEvent.EvidenceReplaced, actorUserId, evidence.Id, cmd.ApplicationId, evidence.Type, ct);
        await _db.SaveChangesAsync(ct);

        return Result.Success();
    }

    // ---------------------------------------------------------------- allocate

    public async Task<Result> AllocateAsync(AllocateEvidenceCommand cmd, string actorUserId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(cmd);
        ArgumentException.ThrowIfNullOrWhiteSpace(actorUserId);

        var evidence = await _db.Evidence.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == cmd.EvidenceId && e.ApplicationId == cmd.ApplicationId, ct);
        if (evidence is null)
        {
            return Result.Failure(new DomainError(EvidenceReasons.Codes.NotFound, null, EvidenceReasons.NotFound));
        }

        // Orphan guard: after this replace-all, the evidence must still link ≥1 line OR a disbursement.
        var validation = await ValidateWriteAsync(cmd.ApplicationId, evidence.Amount, evidence.Currency,
            evidence.DocumentReferenceNumber, evidence.DisbursementId, cmd.Lines, requireLineOrDisbursement: true, ct);
        if (validation is not null)
        {
            return Result.Failure(validation);
        }

        var lockError = await EnsureEvidenceUnlockedAsync(cmd.EvidenceId, ct);
        if (lockError is not null)
        {
            return Result.Failure(lockError);
        }
        // A line becoming a target must itself be open (checked against the incoming lines too).
        var newLineLockError = await EnsureLinesUnlockedAsync(cmd.Lines.Select(l => l.ItemId), ct);
        if (newLineLockError is not null)
        {
            return Result.Failure(newLineLockError);
        }

        await ReplaceAllocationsAsync(cmd.EvidenceId, cmd.Lines, ct);

        await WriteAuditAsync(AdminAuditEvent.EvidenceAllocated, actorUserId, cmd.EvidenceId, cmd.ApplicationId, evidence.Type, ct);

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Failure(new DomainError(EvidenceReasons.Codes.Concurrency, null, EvidenceReasons.Concurrency));
        }

        return Result.Success();
    }

    // ---------------------------------------------------------------- delete

    public async Task<Result> DeleteAsync(int applicationId, int evidenceId, string actorUserId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actorUserId);

        var evidence = await _db.Evidence
            .Include(e => e.Versions)
            .FirstOrDefaultAsync(e => e.Id == evidenceId && e.ApplicationId == applicationId, ct);
        if (evidence is null)
        {
            return Result.Failure(new DomainError(EvidenceReasons.Codes.NotFound, null, EvidenceReasons.NotFound));
        }

        var lockError = await EnsureEvidenceUnlockedAsync(evidenceId, ct);
        if (lockError is not null)
        {
            return Result.Failure(lockError);
        }

        // Capture blob keys before delete (each version may point at a distinct blob).
        var blobKeys = evidence.Versions.Select(v => v.BlobKey)
            .Append(evidence.BlobKey).Distinct().ToList();

        // Remove the M:N allocation rows explicitly (the DB FK CASCADE also covers this on real SQL,
        // but the rows are a separate DbSet — not an owned navigation — so remove them here too).
        var allocations = await _db.EvidenceLineAllocations.Where(a => a.EvidenceId == evidenceId).ToListAsync(ct);
        _db.EvidenceLineAllocations.RemoveRange(allocations);
        _db.Evidence.Remove(evidence); // cascades the owned version chain

        await WriteAuditAsync(AdminAuditEvent.EvidenceDeleted, actorUserId, evidenceId, applicationId, evidence.Type, ct);

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Failure(new DomainError(EvidenceReasons.Codes.Concurrency, null, EvidenceReasons.Concurrency));
        }

        foreach (var bk in blobKeys)
        {
            await DeleteBlobBestEffortAsync(bk, ct);
        }

        return Result.Success();
    }

    // ---------------------------------------------------------------- validation helpers

    /// <summary>Common write validation: amount &gt; 0, CRC, reference present, orphan guard (when
    /// required), lines belong to the application, and Σ line allocations ≤ amount. Returns the first
    /// error, or null.</summary>
    private async Task<DomainError?> ValidateWriteAsync(
        int applicationId, decimal amount, string currency, string documentReferenceNumber,
        int? disbursementId, IReadOnlyList<EvidenceLineAllocationInput>? lines,
        bool requireLineOrDisbursement, CancellationToken ct)
    {
        if (amount <= 0m)
        {
            return new DomainError(EvidenceReasons.Codes.AmountInvalid, "Amount", EvidenceReasons.AmountNotPositive);
        }
        if (currency is null || !string.Equals(currency.Trim(), EvidenceEntity.RequiredCurrency, StringComparison.OrdinalIgnoreCase))
        {
            return new DomainError(EvidenceReasons.Codes.NonCrc, "Currency", EvidenceReasons.NonCrcCurrency);
        }
        if (string.IsNullOrWhiteSpace(documentReferenceNumber))
        {
            return new DomainError(EvidenceReasons.Codes.InvalidInput, "DocumentReferenceNumber", EvidenceReasons.InvalidInput);
        }

        var lineList = lines ?? [];
        if (requireLineOrDisbursement && lineList.Count == 0 && disbursementId is null)
        {
            return new DomainError(EvidenceReasons.Codes.Orphaned, null, EvidenceReasons.Orphaned);
        }

        if (lineList.Count > 0)
        {
            if (lineList.Any(l => l.Amount <= 0m))
            {
                return new DomainError(EvidenceReasons.Codes.InvalidInput, null, EvidenceReasons.InvalidInput);
            }
            // Σ allocations ≤ amount (allocation integrity, D2).
            if (lineList.Sum(l => l.Amount) > amount)
            {
                return new DomainError(EvidenceReasons.Codes.AllocationExceedsAmount, null, EvidenceReasons.AllocationExceedsAmount);
            }
            // Every target line must belong to the application (≤1 row per line — collapse dups).
            var ids = lineList.Select(l => l.ItemId).Distinct().ToList();
            if (ids.Count != lineList.Count)
            {
                return new DomainError(EvidenceReasons.Codes.InvalidInput, null, EvidenceReasons.InvalidInput);
            }
            var found = await _db.Items.AsNoTracking()
                .CountAsync(i => i.ApplicationId == applicationId && ids.Contains(i.Id), ct);
            if (found != ids.Count)
            {
                return new DomainError(EvidenceReasons.Codes.LineNotFound, null, EvidenceReasons.LineNotFound);
            }
        }

        return null;
    }

    /// <summary>Spec 047 / US3 (T049) — refuse an evidence write when any line the evidence is
    /// allocated to is closed. In US1 no line can be closed, so this is inert until closure ships;
    /// implemented as a query so US3 needs no re-wiring here.</summary>
    private Task<DomainError?> EnsureEvidenceUnlockedAsync(int evidenceId, CancellationToken ct)
        => EnsureLinesUnlockedAsync(
            _db.EvidenceLineAllocations.Where(a => a.EvidenceId == evidenceId).Select(a => a.ItemId), ct);

    private async Task<DomainError?> EnsureLinesUnlockedAsync(IEnumerable<int> itemIds, CancellationToken ct)
    {
        var ids = itemIds.Distinct().ToList();
        if (ids.Count == 0)
        {
            return null;
        }
        var anyClosed = await _db.Items.AsNoTracking()
            .AnyAsync(i => ids.Contains(i.Id) && i.ClosureState == ItemClosureState.Closed, ct);
        return anyClosed
            ? new DomainError(EvidenceReasons.Codes.EvidenceLocked, null, EvidenceReasons.EvidenceLocked)
            : null;
    }

    private async Task ReplaceAllocationsAsync(int evidenceId, IReadOnlyList<EvidenceLineAllocationInput> lines, CancellationToken ct)
    {
        var existing = await _db.EvidenceLineAllocations.Where(a => a.EvidenceId == evidenceId).ToListAsync(ct);
        _db.EvidenceLineAllocations.RemoveRange(existing);
        foreach (var l in lines)
        {
            _db.EvidenceLineAllocations.Add(EvidenceLineAllocation.For(evidenceId, l.ItemId, l.Amount));
        }
    }

    // ---------------------------------------------------------------- projections + misc helpers

    private async Task<IReadOnlyList<EvidenceVersionRow>> VersionRowsAsync(int evidenceId, CancellationToken ct)
    {
        var rows = await _db.EvidenceVersions.AsNoTracking()
            .Where(v => v.EvidenceId == evidenceId)
            .OrderByDescending(v => v.VersionNumber)
            .Select(v => new
            {
                v.VersionNumber, v.IsCurrent, v.OriginalFileName, v.Amount, v.Currency,
                v.DocumentReferenceNumber, v.DocumentDate, v.FileHash, v.Reason, v.CreatedByUserId, v.CreatedAtUtc,
            })
            .ToListAsync(ct);

        var names = await ResolveDisplayNamesAsync(rows.Select(r => r.CreatedByUserId).Distinct().ToList(), ct);

        return rows.Select(v => new EvidenceVersionRow(
            v.VersionNumber, v.IsCurrent, v.OriginalFileName, v.Amount, v.Currency,
            v.DocumentReferenceNumber, v.DocumentDate, v.FileHash, v.Reason,
            Lookup(names, v.CreatedByUserId), v.CreatedAtUtc)).ToList();
    }

    private Task WriteAuditAsync(string eventKind, string actorUserId, int evidenceId, int applicationId, EvidenceType type, CancellationToken ct)
        => _audit.WriteAsync(
            eventKind, actorUserId,
            JsonSerializer.Serialize(new { evidenceId, applicationId, type = type.ToString() }),
            ct);

    private static async Task<string> ComputeSha256Async(Stream content, CancellationToken ct)
    {
        content.Position = 0;
        using var sha = SHA256.Create();
        var hash = await sha.ComputeHashAsync(content, ct);
        content.Position = 0;
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string LineLabel(string? lineCode, string productName, int itemId)
        => !string.IsNullOrWhiteSpace(lineCode) ? lineCode!
            : !string.IsNullOrWhiteSpace(productName) ? productName
            : $"L-{itemId.ToString(CultureInfo.InvariantCulture)}";

    private async Task DeleteBlobBestEffortAsync(string blobKey, CancellationToken ct)
    {
        try
        {
            await _storage.DeleteAsync(Category, ObjectKey.Parse(blobKey), ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Best-effort delete of evidence blob {BlobKey} failed; it may be leaked.", blobKey);
        }
    }

    private async Task<Dictionary<string, string>> ResolveDisplayNamesAsync(IReadOnlyList<string> userIds, CancellationToken ct)
    {
        if (userIds.Count == 0)
        {
            return [];
        }
        var users = await _db.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .Select(u => new { u.Id, u.FirstName, u.LastName, u.Email })
            .ToListAsync(ct);
        return users.ToDictionary(u => u.Id, u => ComposeDisplayName(u.FirstName, u.LastName, u.Email));
    }

    private async Task<string> ResolveDisplayNameAsync(string userId, CancellationToken ct)
    {
        var u = await _db.Users.AsNoTracking()
            .Where(x => x.Id == userId)
            .Select(x => new { x.FirstName, x.LastName, x.Email })
            .FirstOrDefaultAsync(ct);
        return u is null ? string.Empty : ComposeDisplayName(u.FirstName, u.LastName, u.Email);
    }

    private static string Lookup(Dictionary<string, string> names, string userId)
        => names.TryGetValue(userId, out var n) ? n : string.Empty;

    private static string ComposeDisplayName(string? firstName, string? lastName, string? email)
    {
        var full = $"{firstName} {lastName}".Trim();
        return full.Length > 0 ? full : (email ?? string.Empty);
    }
}
