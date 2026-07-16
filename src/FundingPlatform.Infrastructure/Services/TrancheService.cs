// Spec 046 — see specs/046-tranches-budget-lines/contracts/interfaces.md §1 and research D7/D8.

using System.Text.Json;
using FundingPlatform.Application.Abstractions;
using FundingPlatform.Application.Admin.Companies; // CompanyNameNormalizer (accent/case fold)
using FundingPlatform.Application.Admin.Users.DTOs;
using FundingPlatform.Application.Disbursements; // DisbursementReasons (service-produced es-CR)
using FundingPlatform.Application.Services;       // ApplicationCurrencyTotal.LineBudget
using FundingPlatform.Application.Tranches;
using FundingPlatform.Domain.Entities;
using FundingPlatform.Domain.Enums;
using FundingPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FundingPlatform.Infrastructure.Services;

/// <summary>
/// Spec 046 / US1 — implements <see cref="ITrancheService"/>. Mirrors <c>FundService</c> for the
/// two-SaveChanges audit discipline. Tranche CRUD + assignment route through the <c>Application</c>
/// aggregate (freeze + sibling-uniqueness invariants); the accent/case duplicate pre-check uses
/// <see cref="CompanyNameNormalizer"/> and the <c>UX_Tranches_ApplicationId_Name</c> index backstops
/// races. Group-scope + role authorization is the controller's job.
/// </summary>
public sealed class TrancheService : ITrancheService
{
    private readonly AppDbContext _db;
    private readonly IAdminAuditEventWriter _audit;

    public TrancheService(AppDbContext db, IAdminAuditEventWriter audit)
    {
        _db = db;
        _audit = audit;
    }

    // ---------------------------------------------------------------- reads

    public async Task<IReadOnlyList<TrancheView>> GetForApplicationAsync(int applicationId, CancellationToken ct)
    {
        var app = await LoadWithBudgetsAsync(applicationId, ct);
        if (app is null)
        {
            return [];
        }

        return app.Tranches
            .OrderBy(t => t.Ordinal).ThenBy(t => t.Id)
            .Select(t =>
            {
                var members = app.Items.Where(i => i.TrancheId == t.Id).ToList();
                return new TrancheView(
                    t.Id, t.Name, t.Ordinal,
                    members.Sum(ApplicationCurrencyTotal.LineBudget),
                    members.Select(i => i.Id).ToList());
            })
            .ToList();
    }

    public async Task<IReadOnlyList<TrancheEditorLine>> GetEditorLinesAsync(int applicationId, CancellationToken ct)
    {
        var app = await LoadWithBudgetsAsync(applicationId, ct);
        if (app is null)
        {
            return [];
        }

        return app.Items
            .OrderBy(i => i.LineCode ?? string.Empty).ThenBy(i => i.Id)
            .Select(i => new TrancheEditorLine(
                i.Id, i.LineCode, i.ProductName, ApplicationCurrencyTotal.LineBudget(i), i.TrancheId))
            .ToList();
    }

    // ---------------------------------------------------------------- mutations

    public async Task<Result<int>> CreateAsync(int applicationId, string name, string actorUserId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actorUserId);

        var app = await LoadForMutationAsync(applicationId, ct);
        if (app is null)
        {
            return Result<int>.Failure(NotFound());
        }
        if (Frozen(app, out var frozen))
        {
            return Result<int>.Failure(frozen);
        }
        if (DuplicateName(app, name, excludeTrancheId: null))
        {
            return Result<int>.Failure(NameInUse());
        }

        Tranche tranche;
        try
        {
            tranche = app.CreateTranche(name);
        }
        catch (ArgumentException ex)
        {
            return Result<int>.Failure(new DomainError(DisbursementReasons.Codes.InvalidInput, "name", ex.Message));
        }

        var saved = await SaveWithNameGuardAsync(ct);
        if (saved is not null)
        {
            return Result<int>.Failure(saved);
        }

        await WriteAuditAsync(AdminAuditEvent.TrancheCreated, actorUserId,
            new { trancheId = tranche.Id, applicationId, name = tranche.Name }, ct);
        await _db.SaveChangesAsync(ct);

        return Result<int>.Success(tranche.Id);
    }

    public async Task<Result> RenameAsync(int applicationId, int trancheId, string name, string actorUserId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actorUserId);

        var app = await LoadForMutationAsync(applicationId, ct);
        if (app is null)
        {
            return Result.Failure(NotFound());
        }
        if (Frozen(app, out var frozen))
        {
            return Result.Failure(frozen);
        }
        var tranche = app.Tranches.FirstOrDefault(t => t.Id == trancheId);
        if (tranche is null)
        {
            return Result.Failure(TrancheNotFound());
        }
        if (DuplicateName(app, name, excludeTrancheId: trancheId))
        {
            return Result.Failure(NameInUse());
        }

        var oldName = tranche.Name;
        try
        {
            app.RenameTranche(trancheId, name);
        }
        catch (ArgumentException ex)
        {
            return Result.Failure(new DomainError(DisbursementReasons.Codes.InvalidInput, "name", ex.Message));
        }

        var saved = await SaveWithNameGuardAsync(ct);
        if (saved is not null)
        {
            return Result.Failure(saved);
        }

        await WriteAuditAsync(AdminAuditEvent.TrancheRenamed, actorUserId,
            new { trancheId, applicationId, oldName, newName = tranche.Name }, ct);
        await _db.SaveChangesAsync(ct);

        return Result.Success();
    }

    public async Task<Result> DeleteAsync(int applicationId, int trancheId, string actorUserId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actorUserId);

        var app = await LoadForMutationAsync(applicationId, ct);
        if (app is null)
        {
            return Result.Failure(NotFound());
        }
        if (Frozen(app, out var frozen))
        {
            return Result.Failure(frozen);
        }
        var tranche = app.Tranches.FirstOrDefault(t => t.Id == trancheId);
        if (tranche is null)
        {
            return Result.Failure(TrancheNotFound());
        }

        var name = tranche.Name;
        app.DeleteTranche(trancheId); // re-parents member lines to TrancheId=null (synthetic)

        await _db.SaveChangesAsync(ct);

        await WriteAuditAsync(AdminAuditEvent.TrancheDeleted, actorUserId,
            new { trancheId, applicationId, name }, ct);
        await _db.SaveChangesAsync(ct);

        return Result.Success();
    }

    public async Task<Result> AssignItemAsync(int applicationId, int itemId, int? trancheId, string actorUserId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actorUserId);

        var app = await LoadForMutationAsync(applicationId, ct);
        if (app is null)
        {
            return Result.Failure(NotFound());
        }
        if (Frozen(app, out var frozen))
        {
            return Result.Failure(frozen);
        }
        if (app.Items.All(i => i.Id != itemId))
        {
            return Result.Failure(LineNotFound());
        }
        if (trancheId is { } tid && app.Tranches.All(t => t.Id != tid))
        {
            return Result.Failure(TrancheNotFound());
        }

        app.AssignItemToTranche(itemId, trancheId);
        await _db.SaveChangesAsync(ct);

        await WriteAuditAsync(
            trancheId is null ? AdminAuditEvent.TrancheItemUnassigned : AdminAuditEvent.TrancheItemAssigned,
            actorUserId,
            new { trancheId, applicationId, itemId }, ct);
        await _db.SaveChangesAsync(ct);

        return Result.Success();
    }

    // ---------------------------------------------------------------- helpers

    private Task<AppEntity?> LoadForMutationAsync(int applicationId, CancellationToken ct)
        => _db.Applications
            .Include(a => a.Items)
            .Include(a => a.Tranches)
            .FirstOrDefaultAsync(a => a.Id == applicationId, ct);

    private Task<AppEntity?> LoadWithBudgetsAsync(int applicationId, CancellationToken ct)
        => _db.Applications.AsNoTracking()
            .Include(a => a.Items).ThenInclude(i => i.Quotations)
            .Include(a => a.Tranches)
            .FirstOrDefaultAsync(a => a.Id == applicationId, ct);

    /// <summary>True when the application is executed (tranche structure frozen, D4).</summary>
    private static bool Frozen(AppEntity app, out DomainError error)
    {
        if (app.State == ApplicationState.AgreementExecuted)
        {
            error = new DomainError(DisbursementReasons.Codes.TrancheFrozen, null, DisbursementReasons.TrancheFrozen);
            return true;
        }
        error = null!;
        return false;
    }

    private static bool DuplicateName(AppEntity app, string name, int? excludeTrancheId)
        => app.Tranches
            .Where(t => excludeTrancheId is null || t.Id != excludeTrancheId)
            .Any(t => CompanyNameNormalizer.AreEquivalent(t.Name, name));

    /// <summary>SaveChanges, translating the unique-index race (concurrent duplicate name) into
    /// <c>TrancheNameInUse</c>. Returns null on success, else the error.</summary>
    private async Task<DomainError?> SaveWithNameGuardAsync(CancellationToken ct)
    {
        try
        {
            await _db.SaveChangesAsync(ct);
            return null;
        }
        catch (DbUpdateException)
        {
            return NameInUse();
        }
    }

    private Task WriteAuditAsync(string action, string actorUserId, object payload, CancellationToken ct)
        => _audit.WriteAsync(action, actorUserId, JsonSerializer.Serialize(payload), ct);

    private static DomainError NotFound()
        => new(DisbursementReasons.Codes.NotFound, null, "No se encontró la solicitud.");
    private static DomainError NameInUse()
        => new(DisbursementReasons.Codes.TrancheNameInUse, "name", DisbursementReasons.TrancheNameInUse);
    private static DomainError TrancheNotFound()
        => new(DisbursementReasons.Codes.TrancheNotFound, null, DisbursementReasons.TrancheNotFound);
    private static DomainError LineNotFound()
        => new(DisbursementReasons.Codes.LineNotFound, null, DisbursementReasons.LineNotFound);
}
