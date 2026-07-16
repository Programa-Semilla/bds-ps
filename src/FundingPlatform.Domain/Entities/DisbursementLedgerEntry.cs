using FundingPlatform.Domain.Enums;

namespace FundingPlatform.Domain.Entities;

/// <summary>
/// Spec 045 / FR-017/FR-018 — an append-only, immutable record of a committed financial
/// fact. The ledger holds only committed facts: exactly one <see cref="LedgerEntryType.Allocation"/>
/// entry per application (the approved ceiling, snapshotted at first disbursement) plus one
/// immutable <see cref="LedgerEntryType.Disbursement"/> entry per validated disbursement
/// (posted at the moment of validation). This is the crux invariant made physical —
/// balances are derived from the ledger, never from a stored mutable value (research R3).
///
/// The type has <b>no instance mutators</b>: append-only is enforced by construction
/// (only static factories) and by service discipline (never updated or deleted).
/// </summary>
public sealed class DisbursementLedgerEntry
{
    public int Id { get; private set; }
    public int ApplicationId { get; private set; }
    public LedgerEntryType EntryType { get; private set; }
    public decimal Amount { get; private set; }
    public int? DisbursementId { get; private set; }
    public string PostedByUserId { get; private set; } = string.Empty;
    public DateTimeOffset PostedAtUtc { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    private DisbursementLedgerEntry() { } // EF

    /// <summary>FR-018 — the one-time Allocation snapshot posted at first disbursement.
    /// Uniqueness (one per application) is backstopped by the filtered unique index.</summary>
    public static DisbursementLedgerEntry Allocation(int applicationId, decimal amount, string postedByUserId)
    {
        if (applicationId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(applicationId));
        }
        if (amount < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), "Allocation amount must not be negative.");
        }
        if (string.IsNullOrWhiteSpace(postedByUserId))
        {
            throw new ArgumentException("PostedByUserId is required.", nameof(postedByUserId));
        }

        return new DisbursementLedgerEntry
        {
            ApplicationId = applicationId,
            EntryType = LedgerEntryType.Allocation,
            Amount = amount,
            DisbursementId = null,
            PostedByUserId = postedByUserId,
            PostedAtUtc = DateTimeOffset.UtcNow,
        };
    }

    /// <summary>FR-018 — the immutable Disbursement entry posted at the moment of validation.
    /// Uniqueness (one per validated disbursement) is backstopped by the filtered unique index.</summary>
    public static DisbursementLedgerEntry ForValidatedDisbursement(Disbursement disbursement, string postedByUserId)
    {
        ArgumentNullException.ThrowIfNull(disbursement);
        if (disbursement.State != DisbursementState.Validated)
        {
            throw new InvalidOperationException(
                $"A Disbursement ledger entry may only be posted for a Validated disbursement; disbursement {disbursement.Id} is {disbursement.State}.");
        }
        if (string.IsNullOrWhiteSpace(postedByUserId))
        {
            throw new ArgumentException("PostedByUserId is required.", nameof(postedByUserId));
        }

        return new DisbursementLedgerEntry
        {
            ApplicationId = disbursement.ApplicationId,
            EntryType = LedgerEntryType.Disbursement,
            Amount = disbursement.Amount,
            DisbursementId = disbursement.Id,
            PostedByUserId = postedByUserId,
            PostedAtUtc = DateTimeOffset.UtcNow,
        };
    }
}
