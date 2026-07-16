// Spec 045 — see specs/045-financial-disbursement-core/contracts/interfaces.md and research R1/R3/R5/R7/R8.

using System.Globalization;
using System.Text.Json;
using FundingPlatform.Application.Abstractions;
using FundingPlatform.Application.Abstractions.Storage;
using FundingPlatform.Application.Admin.Users.DTOs;
using FundingPlatform.Application.Disbursements;
using FundingPlatform.Application.Services;
using FundingPlatform.Domain.Entities;
using FundingPlatform.Domain.Enums;
using FundingPlatform.Domain.Services;
using FundingPlatform.Domain.ValueObjects;
using FundingPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
// Disambiguate the domain entity from the Application-layer namespace of the same name.
using DisbursementEntity = FundingPlatform.Domain.Entities.Disbursement;
using EvidenceEntity = FundingPlatform.Domain.Entities.DisbursementEvidence;

namespace FundingPlatform.Infrastructure.Services;

/// <summary>
/// Spec 045 — implements <see cref="IDisbursementService"/>. Mirrors
/// <c>FundsUsageEvidenceService</c> for storage + the two-SaveChanges audit discipline.
/// The pure <see cref="DisbursementReconciliation"/> evaluator holds the reconciliation
/// rules; the append-only <see cref="DisbursementLedgerEntry"/> ledger is the balance
/// source (research R3). Group-scope + role authorization is the controller's job.
/// </summary>
public sealed class DisbursementService : IDisbursementService
{
    private const FileCategory Category = FileCategory.DisbursementEvidence;

    private readonly AppDbContext _db;
    private readonly IObjectStorage _storage;
    private readonly IAdminAuditEventWriter _audit;
    private readonly ILogger<DisbursementService> _logger;

    public DisbursementService(
        AppDbContext db,
        IObjectStorage storage,
        IAdminAuditEventWriter audit,
        ILogger<DisbursementService> logger)
    {
        _db = db;
        _storage = storage;
        _audit = audit;
        _logger = logger;
    }

    // ---------------------------------------------------------------- reads

    public async Task<IReadOnlyList<DisbursementListItem>> ListAsync(int applicationId, CancellationToken ct)
    {
        var rows = await _db.Disbursements.AsNoTracking()
            .Where(d => d.ApplicationId == applicationId)
            .OrderByDescending(d => d.CreatedAtUtc).ThenByDescending(d => d.Id)
            .Select(d => new
            {
                d.Id,
                d.PaymentDate,
                d.Amount,
                d.State,
                HasBankReceipt = _db.DisbursementEvidence.Any(e => e.DisbursementId == d.Id && e.Kind == EvidenceKind.BankReceipt),
                HasInvoice = _db.DisbursementEvidence.Any(e => e.DisbursementId == d.Id && e.Kind == EvidenceKind.Invoice),
            })
            .ToListAsync(ct);

        return rows
            .Select(r => new DisbursementListItem(
                r.Id, r.PaymentDate, r.Amount, r.State, r.HasBankReceipt, r.HasInvoice))
            .ToList();
    }

    public async Task<DisbursementDetail?> GetAsync(int applicationId, int disbursementId, CancellationToken ct)
    {
        var d = await _db.Disbursements.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == disbursementId && x.ApplicationId == applicationId, ct);
        if (d is null)
        {
            return null;
        }

        var evidence = await _db.DisbursementEvidence.AsNoTracking()
            .Where(e => e.DisbursementId == disbursementId)
            .Join(_db.Users.AsNoTracking(), e => e.UploadedByUserId, u => u.Id, (e, u) => new { e, u })
            .ToListAsync(ct);

        var createdBy = await ResolveDisplayNameAsync(d.CreatedByUserId, ct);

        var bank = evidence.Where(x => x.e.Kind == EvidenceKind.BankReceipt).Select(x => (decimal?)x.e.Amount).FirstOrDefault();
        var invoice = evidence.Where(x => x.e.Kind == EvidenceKind.Invoice).Select(x => (decimal?)x.e.Amount).FirstOrDefault();
        var sum = await SumNonCancelledAsync(applicationId, excludeId: null, ct);
        var allocation = await GetOrComputeAllocationAsync(applicationId, ct);

        var discrepancies = DisbursementReconciliation.Evaluate(d.Amount, bank, invoice, sum, allocation);

        var evidenceSummaries = evidence
            .OrderBy(x => x.e.Kind)
            .Select(x => new DisbursementEvidenceSummary(
                x.e.Kind, x.e.Amount, x.e.Currency, x.e.DocumentReferenceNumber, x.e.DocumentDate,
                x.e.OriginalFileName, ComposeDisplayName(x.u.FirstName, x.u.LastName, x.u.Email), x.e.UploadedAtUtc))
            .ToList();

        var isValidatable = d.State is not (DisbursementState.Validated or DisbursementState.Cancelled)
                            && bank.HasValue && invoice.HasValue && discrepancies.Count == 0;

        return new DisbursementDetail(
            d.Id, d.ApplicationId, d.PaymentDate, d.Amount, d.BankTransactionReference, d.BankAccountReference,
            d.State, createdBy, d.CreatedAtUtc, d.ValidatedAtUtc, evidenceSummaries, discrepancies, isValidatable);
    }

    // ---------------------------------------------------------------- record

    public async Task<Result<int>> RecordAsync(RecordDisbursementCommand cmd, string actorUserId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(cmd);
        ArgumentException.ThrowIfNullOrWhiteSpace(actorUserId);

        // Light load for the gate + factory (Record reads only State/Id). The heavy
        // Items→Quotations graph is loaded below only when the allocation must be computed.
        var app = await _db.Applications.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == cmd.ApplicationId, ct);
        if (app is null)
        {
            return Result<int>.Failure(new DomainError(DisbursementReasons.Codes.NotFound, null, DisbursementReasons.NotFound));
        }

        var errors = new List<DomainError>();
        if (app.State != ApplicationState.AgreementExecuted)
        {
            errors.Add(new DomainError(DisbursementReasons.Codes.NotExecuted, null, DisbursementReasons.NotExecuted));
        }
        if (cmd.Amount <= 0m)
        {
            errors.Add(new DomainError(DisbursementReasons.Codes.AmountInvalid, nameof(cmd.Amount), DisbursementReasons.AmountNotPositive));
        }
        if (string.IsNullOrWhiteSpace(cmd.BankTransactionReference))
        {
            errors.Add(new DomainError(DisbursementReasons.Codes.InvalidInput, nameof(cmd.BankTransactionReference), DisbursementReasons.BankTransactionRequired));
        }
        if (errors.Count > 0)
        {
            return Result<int>.Failure(errors);
        }

        // Spec 046 — when a per-line split is supplied, every target line must be committed and the
        // split must sum to the amount (split integrity, FR-013). Skipped for a flat P1 disbursement.
        if (cmd.Lines is { Count: > 0 })
        {
            var lineError = await ValidateLinesAsync(cmd.ApplicationId, cmd.Amount, cmd.Lines, ct);
            if (lineError is not null)
            {
                return Result<int>.Failure(lineError);
            }
        }

        // Ensure the one-time Allocation snapshot exists (idempotent — filtered-unique
        // index backstops the race). Reuse the canonical CRC rollup (research R1).
        var allocationExists = await _db.DisbursementLedgerEntries
            .AnyAsync(l => l.ApplicationId == cmd.ApplicationId && l.EntryType == LedgerEntryType.Allocation, ct);
        if (!allocationExists)
        {
            // Only the first disbursement needs the heavy Items→Quotations graph to snapshot
            // the canonical CRC rollup; later records short-circuit on the ledger entry (P2 perf).
            var appForTotal = await _db.Applications.AsNoTracking()
                .Include(a => a.Items).ThenInclude(i => i.Quotations)
                .FirstOrDefaultAsync(a => a.Id == cmd.ApplicationId, ct);
            var allocationAmount = appForTotal is null ? 0m : ApplicationCurrencyTotal.Compute(appForTotal).Total ?? 0m;
            _db.DisbursementLedgerEntries.Add(
                DisbursementLedgerEntry.Allocation(cmd.ApplicationId, allocationAmount, actorUserId));
        }

        var disbursement = DisbursementEntity.Record(
            app, actorUserId, cmd.PaymentDate, cmd.Amount, cmd.BankTransactionReference, cmd.BankAccountReference);
        _db.Disbursements.Add(disbursement);

        // Early over-disbursement signal (FR-005 / US3). No evidence yet, so only
        // comparison (c) can flag. The authoritative race-proof gate is in ValidateAsync.
        var allocation = await GetOrComputeAllocationAsync(cmd.ApplicationId, ct);
        var existingSum = await SumNonCancelledAsync(cmd.ApplicationId, excludeId: null, ct);
        var discrepancies = DisbursementReconciliation.Evaluate(
            cmd.Amount, bankReceiptAmount: null, invoiceAmount: null,
            sumOfNonCancelledIncludingThis: existingSum + cmd.Amount, allocation);
        disbursement.ApplyReconciliation(discrepancies);

        try
        {
            await _db.SaveChangesAsync(ct); // assigns disbursement.Id for the audit payload + splits
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result<int>.Failure(new DomainError(DisbursementReasons.Codes.Concurrency, null, DisbursementReasons.Concurrency));
        }
        catch (DbUpdateException)
        {
            // Two operators recording the very first disbursement concurrently both try to insert
            // the one-and-only Allocation entry; the filtered-unique UX_DisbursementLedger_Allocation
            // rejects the loser as a DbUpdateException (not a concurrency exception). Surface it as a
            // retryable concurrency error rather than a 500 — the retry finds the entry present.
            return Result<int>.Failure(new DomainError(DisbursementReasons.Codes.Concurrency, null, DisbursementReasons.Concurrency));
        }

        // Spec 046 — persist the per-line split now that the disbursement id exists.
        if (cmd.Lines is { Count: > 0 })
        {
            await ReplaceSplitAsync(disbursement.Id, cmd.Lines, ct);
        }

        await _audit.WriteAsync(
            AdminAuditEvent.DisbursementRecorded, actorUserId,
            JsonSerializer.Serialize(new
            {
                disbursementId = disbursement.Id,
                applicationId = cmd.ApplicationId,
                after = new { amount = cmd.Amount, paymentDate = cmd.PaymentDate.ToString("O"), state = disbursement.State.ToString() },
                lines = cmd.Lines?.Select(l => new { l.ItemId, l.Amount }),
            }),
            ct);
        await _db.SaveChangesAsync(ct);

        return Result<int>.Success(disbursement.Id);
    }

    // ---------------------------------------------------------------- edit

    public async Task<Result> EditAsync(EditDisbursementCommand cmd, string actorUserId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(cmd);
        ArgumentException.ThrowIfNullOrWhiteSpace(actorUserId);

        var d = await _db.Disbursements
            .FirstOrDefaultAsync(x => x.Id == cmd.DisbursementId && x.ApplicationId == cmd.ApplicationId, ct);
        if (d is null)
        {
            return Result.Failure(new DomainError(DisbursementReasons.Codes.NotFound, null, DisbursementReasons.NotFound));
        }
        if (!d.IsPreValidation)
        {
            return Result.Failure(new DomainError(DisbursementReasons.Codes.Locked, null, DisbursementReasons.Locked));
        }
        if (cmd.Amount <= 0m)
        {
            return Result.Failure(new DomainError(DisbursementReasons.Codes.AmountInvalid, nameof(cmd.Amount), DisbursementReasons.AmountNotPositive));
        }
        if (string.IsNullOrWhiteSpace(cmd.BankTransactionReference))
        {
            return Result.Failure(new DomainError(DisbursementReasons.Codes.InvalidInput, nameof(cmd.BankTransactionReference), DisbursementReasons.BankTransactionRequired));
        }

        // Spec 046 — validate the new split (if the caller manages lines) against the new amount.
        if (cmd.Lines is not null)
        {
            var lineError = await ValidateLinesAsync(cmd.ApplicationId, cmd.Amount, cmd.Lines, ct);
            if (lineError is not null)
            {
                return Result.Failure(lineError);
            }
        }

        var before = new { amount = d.Amount, paymentDate = d.PaymentDate.ToString("O"), bankTxn = d.BankTransactionReference, bankAcct = d.BankAccountReference };

        d.EditDetails(cmd.PaymentDate, cmd.Amount, cmd.BankTransactionReference, cmd.BankAccountReference);
        await ReconcileAsync(d, currentAmount: cmd.Amount, ct);

        // Spec 046 — null Lines leaves the existing attribution untouched; non-null replaces it.
        if (cmd.Lines is not null)
        {
            await ReplaceSplitAsync(d.Id, cmd.Lines, ct);
        }

        await _audit.WriteAsync(
            AdminAuditEvent.DisbursementEdited, actorUserId,
            JsonSerializer.Serialize(new
            {
                disbursementId = d.Id,
                applicationId = d.ApplicationId,
                before,
                after = new { amount = cmd.Amount, paymentDate = cmd.PaymentDate.ToString("O"), bankTxn = cmd.BankTransactionReference, bankAcct = cmd.BankAccountReference },
                lines = cmd.Lines?.Select(l => new { l.ItemId, l.Amount }),
            }),
            ct);

        return await CommitAsync(ct);
    }

    // ---------------------------------------------------------------- evidence

    public async Task<Result<int>> AttachEvidenceAsync(AttachDisbursementEvidenceCommand cmd, string actorUserId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(cmd);
        ArgumentException.ThrowIfNullOrWhiteSpace(actorUserId);

        var d = await _db.Disbursements
            .FirstOrDefaultAsync(x => x.Id == cmd.DisbursementId && x.ApplicationId == cmd.ApplicationId, ct);
        if (d is null)
        {
            return Result<int>.Failure(new DomainError(DisbursementReasons.Codes.NotFound, null, DisbursementReasons.NotFound));
        }
        if (!d.IsPreValidation)
        {
            return Result<int>.Failure(new DomainError(DisbursementReasons.Codes.Locked, null, DisbursementReasons.Locked));
        }
        if (cmd.Amount <= 0m)
        {
            return Result<int>.Failure(new DomainError(DisbursementReasons.Codes.AmountInvalid, nameof(cmd.Amount), DisbursementReasons.AmountNotPositive));
        }
        if (cmd.Currency is null || !string.Equals(cmd.Currency.Trim(), EvidenceEntity.RequiredCurrency, StringComparison.OrdinalIgnoreCase))
        {
            return Result<int>.Failure(new DomainError(DisbursementReasons.Codes.NonCrc, nameof(cmd.Currency), DisbursementReasons.NonCrcCurrency));
        }
        if (string.IsNullOrWhiteSpace(cmd.DocumentReferenceNumber))
        {
            return Result<int>.Failure(new DomainError(DisbursementReasons.Codes.InvalidInput, nameof(cmd.DocumentReferenceNumber), DisbursementReasons.DocumentReferenceRequired));
        }

        var existing = await _db.DisbursementEvidence
            .FirstOrDefaultAsync(e => e.DisbursementId == cmd.DisbursementId && e.Kind == cmd.Kind, ct);
        var isReplace = existing is not null;
        var oldBlobKey = existing?.BlobKey;
        // FR-030 — capture the replaced document's prior values before Replace() overwrites them,
        // so the evidence_replaced audit carries before/after (the attach path has no before).
        object? beforeSnapshot = existing is null ? null : new
        {
            amount = existing.Amount,
            currency = existing.Currency,
            reference = existing.DocumentReferenceNumber,
            date = existing.DocumentDate.ToString("O"),
        };

        var ext = Path.GetExtension(cmd.FileName);
        var key = ObjectKey.Build(
            Category,
            ownerSegment: $"application/{cmd.ApplicationId.ToString(CultureInfo.InvariantCulture)}",
            entityId: cmd.DisbursementId.ToString(CultureInfo.InvariantCulture),
            deterministicSuffix: Guid.NewGuid().ToString("N")[..16],
            extension: string.IsNullOrWhiteSpace(ext) ? null : ext);

        await _storage.UploadAsync(Category, key, cmd.Content, cmd.ContentType, cmd.FileSize, ct);

        EvidenceEntity evidence;
        try
        {
            if (existing is not null)
            {
                existing.Replace(d, cmd.Amount, cmd.Currency, cmd.DocumentReferenceNumber, cmd.DocumentDate,
                    cmd.FileName, key.Value, cmd.FileSize, cmd.ContentType, actorUserId);
                evidence = existing;
            }
            else
            {
                evidence = EvidenceEntity.Attach(d, cmd.Kind, cmd.Amount, cmd.Currency, cmd.DocumentReferenceNumber,
                    cmd.DocumentDate, cmd.FileName, key.Value, cmd.FileSize, cmd.ContentType, actorUserId);
                _db.DisbursementEvidence.Add(evidence);
            }
            await _db.SaveChangesAsync(ct); // assigns evidence.Id; commits the evidence row
        }
        catch
        {
            // The row did not commit; the just-uploaded blob would otherwise leak.
            await DeleteBlobBestEffortAsync(key.Value, ct);
            throw;
        }

        // The evidence row now durably points at the new blob (SaveChanges #1). Delete the
        // superseded blob here — before SaveChanges #2 — so a failure persisting the derived
        // state/audit cannot leak the old blob (P3).
        if (isReplace && !string.IsNullOrEmpty(oldBlobKey) && oldBlobKey != key.Value)
        {
            await DeleteBlobBestEffortAsync(oldBlobKey!, ct);
        }

        // Re-run reconciliation now that the evidence amount is committed (FR-016), then
        // persist the derived state + audit in the second SaveChanges.
        await ReconcileAsync(d, currentAmount: d.Amount, ct);

        await _audit.WriteAsync(
            isReplace ? AdminAuditEvent.DisbursementEvidenceReplaced : AdminAuditEvent.DisbursementEvidenceAttached,
            actorUserId,
            JsonSerializer.Serialize(new
            {
                disbursementId = d.Id,
                applicationId = d.ApplicationId,
                evidenceId = evidence.Id,
                kind = cmd.Kind.ToString(),
                before = beforeSnapshot,
                after = new { amount = cmd.Amount, currency = cmd.Currency, reference = cmd.DocumentReferenceNumber, date = cmd.DocumentDate.ToString("O") },
            }),
            ct);
        await _db.SaveChangesAsync(ct);

        return Result<int>.Success(evidence.Id);
    }

    // ---------------------------------------------------------------- validate

    public async Task<Result> ValidateAsync(int applicationId, int disbursementId, string actorUserId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actorUserId);

        var d = await _db.Disbursements
            .FirstOrDefaultAsync(x => x.Id == disbursementId && x.ApplicationId == applicationId, ct);
        if (d is null)
        {
            return Result.Failure(new DomainError(DisbursementReasons.Codes.NotFound, null, DisbursementReasons.NotFound));
        }
        if (!d.IsPreValidation)
        {
            return Result.Failure(new DomainError(DisbursementReasons.Codes.Locked, null, DisbursementReasons.Locked));
        }

        var (bank, invoice, hasBank, hasInvoice) = await EvidenceAmountsAsync(disbursementId, ct);

        // FR-009 — completeness gate names the missing document.
        if (!hasBank || !hasInvoice)
        {
            var reason = (!hasBank, !hasInvoice) switch
            {
                (true, true) => DisbursementReasons.MissingBothEvidence,
                (true, false) => DisbursementReasons.MissingBankReceipt,
                _ => DisbursementReasons.MissingInvoice,
            };
            return Result.Failure(new DomainError(DisbursementReasons.Codes.MissingEvidence, null, reason));
        }

        // Authoritative reconciliation against the freshly-read committed Σ. Comparison (c)
        // here closes the concurrent-partial-payment race single-row concurrency cannot catch
        // (research R5): a disbursement whose own amounts match but whose validation would push
        // the committed total past the allocation is refused with a distinct over-disbursement reason.
        var sum = await SumNonCancelledAsync(applicationId, excludeId: null, ct);
        var allocation = await GetOrComputeAllocationAsync(applicationId, ct);
        var discrepancies = DisbursementReconciliation.Evaluate(d.Amount, bank, invoice, sum, allocation);

        if (discrepancies.Count > 0)
        {
            var overAllocation = discrepancies.Any(x => x.Comparison == ReconciliationComparison.TotalVsAllocation);
            return Result.Failure(overAllocation
                ? new DomainError(DisbursementReasons.Codes.OverAllocation, null, DisbursementReasons.WouldExceedAllocation)
                : new DomainError(DisbursementReasons.Codes.HasDiscrepancy, null, DisbursementReasons.HasDiscrepancy));
        }

        // Spec 046 / FR-019 — per-line over-payment gate, re-checked against FRESHLY-READ committed
        // budgets + non-cancelled payment sums for the lines this disbursement touches (this
        // disbursement is still non-cancelled, so its allocations count). Symmetric with P1's
        // participant-level over-disbursement gate; closes the concurrent-partial-payment race.
        var lineOverpayment = await EvaluateLineOverpaymentsAsync(disbursementId, ct);
        if (lineOverpayment is not null)
        {
            return Result.Failure(lineOverpayment);
        }

        d.Validate(actorUserId, bothEvidencePresent: true, zeroDiscrepancies: true);
        _db.DisbursementLedgerEntries.Add(DisbursementLedgerEntry.ForValidatedDisbursement(d, actorUserId));

        await _audit.WriteAsync(
            AdminAuditEvent.DisbursementValidated, actorUserId,
            JsonSerializer.Serialize(new
            {
                disbursementId = d.Id,
                applicationId = applicationId,
                after = new { amount = d.Amount, state = d.State.ToString() },
            }),
            ct);

        try
        {
            // State flip + immutable ledger entry + audit in one SaveChanges. The filtered-unique
            // UX_DisbursementLedger_Disbursement index backstops a double-post race (FR-018).
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Failure(new DomainError(DisbursementReasons.Codes.Concurrency, null, DisbursementReasons.Concurrency));
        }
        catch (DbUpdateException)
        {
            // A concurrent validation already posted the Disbursement ledger entry for this row.
            return Result.Failure(new DomainError(DisbursementReasons.Codes.Concurrency, null, DisbursementReasons.Concurrency));
        }

        return Result.Success();
    }

    // ---------------------------------------------------------------- cancel

    public async Task<Result> CancelAsync(int applicationId, int disbursementId, string actorUserId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actorUserId);

        var d = await _db.Disbursements
            .FirstOrDefaultAsync(x => x.Id == disbursementId && x.ApplicationId == applicationId, ct);
        if (d is null)
        {
            return Result.Failure(new DomainError(DisbursementReasons.Codes.NotFound, null, DisbursementReasons.NotFound));
        }
        if (!d.IsPreValidation)
        {
            return Result.Failure(new DomainError(DisbursementReasons.Codes.NotPreValidation, null, DisbursementReasons.CannotCancel));
        }

        d.Cancel(actorUserId);

        await _audit.WriteAsync(
            AdminAuditEvent.DisbursementCancelled, actorUserId,
            JsonSerializer.Serialize(new { disbursementId = d.Id, applicationId = applicationId, after = new { state = d.State.ToString() } }),
            ct);

        return await CommitAsync(ct);
    }

    // ---------------------------------------------------------------- commit (spec 046)

    public async Task<Result> CommitLineAsync(int applicationId, int itemId, string actorUserId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actorUserId);

        var app = await _db.Applications.Include(a => a.Items)
            .FirstOrDefaultAsync(a => a.Id == applicationId, ct);
        if (app is null)
        {
            return Result.Failure(new DomainError(DisbursementReasons.Codes.NotFound, null, DisbursementReasons.NotFound));
        }
        if (app.State != ApplicationState.AgreementExecuted)
        {
            return Result.Failure(new DomainError(DisbursementReasons.Codes.NotExecuted, null, DisbursementReasons.NotExecuted));
        }
        if (app.Items.All(i => i.Id != itemId))
        {
            return Result.Failure(new DomainError(DisbursementReasons.Codes.LineNotFound, null, DisbursementReasons.LineNotFound));
        }

        app.CommitLine(itemId); // idempotent
        await _db.SaveChangesAsync(ct);

        await _audit.WriteAsync(
            AdminAuditEvent.LineCommitted, actorUserId,
            JsonSerializer.Serialize(new { itemId, applicationId }), ct);
        await _db.SaveChangesAsync(ct);

        return Result.Success();
    }

    public async Task<Result> UncommitLineAsync(int applicationId, int itemId, string actorUserId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actorUserId);

        var app = await _db.Applications.Include(a => a.Items)
            .FirstOrDefaultAsync(a => a.Id == applicationId, ct);
        if (app is null)
        {
            return Result.Failure(new DomainError(DisbursementReasons.Codes.NotFound, null, DisbursementReasons.NotFound));
        }
        if (app.State != ApplicationState.AgreementExecuted)
        {
            return Result.Failure(new DomainError(DisbursementReasons.Codes.NotExecuted, null, DisbursementReasons.NotExecuted));
        }
        if (app.Items.All(i => i.Id != itemId))
        {
            return Result.Failure(new DomainError(DisbursementReasons.Codes.LineNotFound, null, DisbursementReasons.LineNotFound));
        }

        // FR-007 — a line with any non-cancelled attributed payment cannot be un-committed.
        var hasPayment = await _db.DisbursementLineAllocations.AsNoTracking()
            .AnyAsync(a => a.ItemId == itemId
                && _db.Disbursements.Any(d => d.Id == a.DisbursementId && d.State != DisbursementState.Cancelled), ct);
        if (hasPayment)
        {
            return Result.Failure(new DomainError(DisbursementReasons.Codes.LineHasPayment, null, DisbursementReasons.LineHasPayment));
        }

        app.UncommitLine(itemId); // idempotent
        await _db.SaveChangesAsync(ct);

        await _audit.WriteAsync(
            AdminAuditEvent.LineUncommitted, actorUserId,
            JsonSerializer.Serialize(new { itemId, applicationId }), ct);
        await _db.SaveChangesAsync(ct);

        return Result.Success();
    }

    // ---------------------------------------------------------------- download

    public async Task<DisbursementEvidenceDownload?> OpenEvidenceForDownloadAsync(
        int applicationId, int disbursementId, EvidenceKind kind, CancellationToken ct)
    {
        var row = await _db.DisbursementEvidence.AsNoTracking()
            .Where(e => e.DisbursementId == disbursementId && e.Kind == kind
                        && _db.Disbursements.Any(x => x.Id == disbursementId && x.ApplicationId == applicationId))
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
            _logger.LogWarning("Disbursement evidence (disbursement {DisbursementId}, kind {Kind}) has an unparseable blob key.", disbursementId, kind);
            return null;
        }

        try
        {
            var resolved = await _storage.ResolveServingHandleAsync(Category, key, ServingMode.BackendStream, ct);
            if (resolved is not BackendStreamHandle handle)
            {
                _logger.LogWarning("Disbursement evidence (disbursement {DisbursementId}, kind {Kind}) resolved to a non-backend-stream handle.", disbursementId, kind);
                return null;
            }
            return new DisbursementEvidenceDownload(handle.Content, handle.ContentType ?? row.ContentType, row.OriginalFileName);
        }
        catch (ObjectNotFoundException)
        {
            _logger.LogWarning("Disbursement evidence (disbursement {DisbursementId}, kind {Kind}) row exists but its blob is missing.", disbursementId, kind);
            return null;
        }
    }

    // ---------------------------------------------------------------- spec 046 line-split helpers

    /// <summary>Spec 046 — validate a per-line split: every target line belongs to the application and
    /// is committed (FR-009), and the split sums to the amount (FR-013). Returns the first error, or null.</summary>
    private async Task<DomainError?> ValidateLinesAsync(
        int applicationId, decimal amount, IReadOnlyList<LineAllocationInput> lines, CancellationToken ct)
    {
        if (lines.Any(l => l.Amount <= 0m))
        {
            return new DomainError(DisbursementReasons.Codes.SplitMismatch, null, DisbursementReasons.SplitMismatch);
        }

        var ids = lines.Select(l => l.ItemId).Distinct().ToList();
        var found = await _db.Items.AsNoTracking()
            .Where(i => i.ApplicationId == applicationId && ids.Contains(i.Id))
            .Select(i => new { i.Id, i.CommitState })
            .ToListAsync(ct);

        // Every provided line must belong to the application and be committed.
        if (found.Count != ids.Count || found.Any(i => i.CommitState != ItemCommitState.Committed))
        {
            return new DomainError(DisbursementReasons.Codes.LineNotCommitted, null, DisbursementReasons.LineNotCommitted);
        }

        var split = DisbursementLineReconciliation.EvaluateSplit(
            amount, lines.Select(l => (l.ItemId, l.Amount)).ToList());
        return split.Count > 0
            ? new DomainError(DisbursementReasons.Codes.SplitMismatch, null, DisbursementReasons.SplitMismatch)
            : null;
    }

    /// <summary>Spec 046 — replace-all the disbursement's line allocation rows (mirrors how evidence is
    /// Replaced, not patched). The caller commits.</summary>
    private async Task ReplaceSplitAsync(int disbursementId, IReadOnlyList<LineAllocationInput> lines, CancellationToken ct)
    {
        var existing = await _db.DisbursementLineAllocations
            .Where(a => a.DisbursementId == disbursementId).ToListAsync(ct);
        _db.DisbursementLineAllocations.RemoveRange(existing);
        foreach (var l in lines)
        {
            _db.DisbursementLineAllocations.Add(DisbursementLineAllocation.For(disbursementId, l.ItemId, l.Amount));
        }
    }

    /// <summary>Spec 046 / FR-019 — the per-line over-payment gate. For every line this disbursement
    /// attributes to, read the fresh committed budget + Σ non-cancelled payments to the line and run
    /// the pure evaluator. Returns the first blocking over-payment as a <c>LineOverpayment</c> error
    /// (naming the line), or null when clean / the disbursement is flat (no attributions).</summary>
    private async Task<DomainError?> EvaluateLineOverpaymentsAsync(int disbursementId, CancellationToken ct)
    {
        var touchedItemIds = await _db.DisbursementLineAllocations.AsNoTracking()
            .Where(a => a.DisbursementId == disbursementId)
            .Select(a => a.ItemId)
            .Distinct()
            .ToListAsync(ct);
        if (touchedItemIds.Count == 0)
        {
            return null; // flat disbursement — no line dimension to check
        }

        // Fresh committed budgets (LineBudget LINQ twin) for the touched lines.
        var budgets = await _db.Items.AsNoTracking()
            .Where(i => touchedItemIds.Contains(i.Id))
            .Select(i => new
            {
                i.Id,
                i.LineCode,
                Budget = i.Quotations
                    .Where(q => q.SupplierId == i.SelectedSupplierId && !q.LegacyNeedsReview && q.ConvertedCrcAmount != null)
                    .Select(q => (decimal?)q.ConvertedCrcAmount)
                    .FirstOrDefault() ?? 0m,
            })
            .ToListAsync(ct);

        // Fresh Σ non-cancelled payments per touched line (includes THIS still-non-cancelled disbursement).
        var paidByLine = await _db.DisbursementLineAllocations.AsNoTracking()
            .Where(a => touchedItemIds.Contains(a.ItemId)
                && _db.Disbursements.Any(dd => dd.Id == a.DisbursementId && dd.State != DisbursementState.Cancelled))
            .GroupBy(a => a.ItemId)
            .Select(g => new { ItemId = g.Key, Paid = g.Sum(x => x.Amount) })
            .ToListAsync(ct);

        var checks = budgets.Select(b => new LinePaymentVsBudget(
            b.Id,
            b.LineCode ?? $"L-{b.Id}",
            b.Budget,
            paidByLine.FirstOrDefault(p => p.ItemId == b.Id)?.Paid ?? 0m)).ToList();

        var overpays = DisbursementLineReconciliation.EvaluateLineOverpayments(checks);
        return overpays.Count > 0
            ? new DomainError(DisbursementReasons.Codes.LineOverpayment, null, DisbursementReasons.LineOverpayment(overpays[0].LineLabel))
            : null;
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>Recompute + persist (in-memory) the derived state from a fresh reconciliation.
    /// The caller commits.</summary>
    private async Task ReconcileAsync(DisbursementEntity d, decimal currentAmount, CancellationToken ct)
    {
        var (bank, invoice, _, _) = await EvidenceAmountsAsync(d.Id, ct);
        var otherSum = await SumNonCancelledAsync(d.ApplicationId, excludeId: d.Id, ct);
        var allocation = await GetOrComputeAllocationAsync(d.ApplicationId, ct);
        var discrepancies = DisbursementReconciliation.Evaluate(
            currentAmount, bank, invoice, otherSum + currentAmount, allocation);
        d.ApplyReconciliation(discrepancies);
    }

    private async Task<(decimal? bank, decimal? invoice, bool hasBank, bool hasInvoice)> EvidenceAmountsAsync(int disbursementId, CancellationToken ct)
    {
        var rows = await _db.DisbursementEvidence.AsNoTracking()
            .Where(e => e.DisbursementId == disbursementId)
            .Select(e => new { e.Kind, e.Amount })
            .ToListAsync(ct);
        decimal? bank = rows.Where(r => r.Kind == EvidenceKind.BankReceipt).Select(r => (decimal?)r.Amount).FirstOrDefault();
        decimal? invoice = rows.Where(r => r.Kind == EvidenceKind.Invoice).Select(r => (decimal?)r.Amount).FirstOrDefault();
        return (bank, invoice, bank.HasValue, invoice.HasValue);
    }

    private async Task<decimal> SumNonCancelledAsync(int applicationId, int? excludeId, CancellationToken ct)
    {
        var q = _db.Disbursements.AsNoTracking()
            .Where(d => d.ApplicationId == applicationId && d.State != DisbursementState.Cancelled);
        if (excludeId is { } ex)
        {
            q = q.Where(d => d.Id != ex);
        }
        return await q.SumAsync(d => (decimal?)d.Amount, ct) ?? 0m;
    }

    private Task<decimal> GetOrComputeAllocationAsync(int applicationId, CancellationToken ct)
        => DisbursementAllocation.ResolveAsync(_db, applicationId, ct);

    private async Task<Result> CommitAsync(CancellationToken ct)
    {
        try
        {
            await _db.SaveChangesAsync(ct);
            return Result.Success();
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Failure(new DomainError(DisbursementReasons.Codes.Concurrency, null, DisbursementReasons.Concurrency));
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
            _logger.LogWarning(ex, "Best-effort delete of disbursement evidence blob {BlobKey} failed; it may be leaked.", blobKey);
        }
    }

    private async Task<string> ResolveDisplayNameAsync(string userId, CancellationToken ct)
    {
        var u = await _db.Users.AsNoTracking()
            .Where(x => x.Id == userId)
            .Select(x => new { x.FirstName, x.LastName, x.Email })
            .FirstOrDefaultAsync(ct);
        return u is null ? string.Empty : ComposeDisplayName(u.FirstName, u.LastName, u.Email);
    }

    private static string ComposeDisplayName(string? firstName, string? lastName, string? email)
    {
        var full = $"{firstName} {lastName}".Trim();
        return full.Length > 0 ? full : (email ?? string.Empty);
    }
}
