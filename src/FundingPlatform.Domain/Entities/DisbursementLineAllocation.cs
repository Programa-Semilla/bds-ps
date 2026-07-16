namespace FundingPlatform.Domain.Entities;

/// <summary>
/// Spec 046 — the attribution of a portion of a <see cref="Disbursement"/> to one
/// committed budget-line (<see cref="Item"/>): the M:N join realizing per-line payment
/// attribution. Owned by the <see cref="Disbursement"/>; a split change replaces the row
/// set (mirrors how evidence is Replaced, not patched), so the entity has no mutators.
/// See specs/046-tranches-budget-lines/data-model.md (Aggregate 2).
/// </summary>
public sealed class DisbursementLineAllocation
{
    public int Id { get; private set; }
    public int DisbursementId { get; private set; }
    public int ItemId { get; private set; }
    public decimal Amount { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    private DisbursementLineAllocation() { }

    /// <summary>Creates an attribution row. <paramref name="amount"/> must be &gt; 0
    /// (the split-integrity check and per-line over-payment gate live in
    /// <see cref="Services.DisbursementLineReconciliation"/> and the service).</summary>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="amount"/> ≤ 0.</exception>
    public static DisbursementLineAllocation For(int disbursementId, int itemId, decimal amount)
    {
        if (amount <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), amount, "Line allocation amount must be greater than zero.");
        }
        return new DisbursementLineAllocation
        {
            DisbursementId = disbursementId,
            ItemId = itemId,
            Amount = amount,
        };
    }
}
